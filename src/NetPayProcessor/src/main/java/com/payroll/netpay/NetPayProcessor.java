package com.payroll.netpay;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.payroll.netpay.model.DeductionMap;
import com.payroll.netpay.model.EmployeeInfo;
import com.payroll.netpay.model.NetPayResult;
import com.payroll.netpay.model.TaxConfig;
import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.Record;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Unified processor that computes gross pay and net pay from employee-events.
 *
 * Gross pay aggregation (previously in ksqlDB) is now done here with O(1) upserts
 * into in-memory maps, replacing the unbounded COLLECT_LIST + AS_MAP + REDUCE pattern.
 *
 * State stores:
 * - employeeInfoStore: keyed by employeeId → {payRate, payType, payPeriodHours}
 * - hoursStore: keyed by employeeId:payPeriod → Map<timeEntryId, hoursWorked>
 * - taxConfigStore: keyed by employeeId → tax config
 * - deductionStore: keyed by employeeId → deduction map
 *
 * All stores are static ConcurrentHashMaps shared across processor instances.
 * Safe for this single-instance, single-thread POC.
 */
public class NetPayProcessor implements Processor<String, String, String, String> {

    private static final Logger log = LoggerFactory.getLogger(NetPayProcessor.class);
    private static final ObjectMapper mapper = new ObjectMapper();

    // Pay period epoch: 2024-01-01T00:00:00Z in millis
    static final long PAY_PERIOD_EPOCH_MS = 1704067200000L;
    static final long PAY_PERIOD_DURATION_MS = 14L * 24 * 60 * 60 * 1000; // 14 days

    private static final DateTimeFormatter PAY_PERIOD_FMT =
        DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss");

    // Shared in-memory state
    static final ConcurrentHashMap<String, String> employeeInfoStore = new ConcurrentHashMap<>();
    static final ConcurrentHashMap<String, ConcurrentHashMap<String, Double>> hoursStore = new ConcurrentHashMap<>();
    static final ConcurrentHashMap<String, String> taxConfigStore = new ConcurrentHashMap<>();
    static final ConcurrentHashMap<String, String> deductionStore = new ConcurrentHashMap<>();
    static final Set<String> deactivatedEmployees = Collections.newSetFromMap(new ConcurrentHashMap<>());

    private ProcessorContext<String, String> context;

    @Override
    public void init(ProcessorContext<String, String> context) {
        this.context = context;
    }

    @Override
    public void process(Record<String, String> record) {
        if (record.value() == null) return;

        try {
            JsonNode envelope = mapper.readTree(record.value());
            JsonNode data = envelope.path("data");
            if (data.isMissingNode() || data.isNull()) return;

            JsonNode domainEvents = data.path("DomainEvents");
            if (!domainEvents.isArray() || domainEvents.isEmpty()) return;
            String eventType = domainEvents.get(0).path("EventType").asText("");

            switch (eventType) {
                case "employee.created":
                case "employee.updated":
                    handleEmployeeCreatedOrUpdated(data);
                    break;
                case "employee.deactivated":
                    handleEmployeeDeactivated(data);
                    break;
                case "timeentry.clockedout":
                case "timeentry.updated":
                    handleTimeEntryEvent(data);
                    break;
                default:
                    if (eventType.startsWith("taxinfo.")) {
                        handleTaxInfoEvent(data);
                    } else if (eventType.startsWith("deduction.")) {
                        handleDeductionEvent(data, eventType);
                    }
                    break;
            }
        } catch (Exception e) {
            log.error("Error processing record: {}", e.getMessage(), e);
        }
    }

    private void handleEmployeeCreatedOrUpdated(JsonNode data) throws Exception {
        String employeeId = data.path("Id").asText(null);
        if (employeeId == null) return;

        double payRate = data.path("PayRate").asDouble(0);
        String payType = String.valueOf(data.path("PayType").asInt(1));
        double payPeriodHours = data.path("PayPeriodHours").asDouble(40);

        EmployeeInfo info = new EmployeeInfo(employeeId, payRate, payType, payPeriodHours);
        employeeInfoStore.put(employeeId, mapper.writeValueAsString(info));

        // Compute gross+net pay for the pay period derived from UpdatedAt
        String updatedAt = data.path("UpdatedAt").asText(null);
        if (updatedAt == null) updatedAt = data.path("CreatedAt").asText(null);
        if (updatedAt != null) {
            long payPeriod = computePayPeriodFromTimestamp(updatedAt);
            log.info("Employee info updated: employee={}, payRate={}, payType={}, payPeriodHours={}, period={}",
                employeeId, payRate, payType, payPeriodHours, payPeriod);
            computeAndEmit(employeeId, payPeriod);
        }
    }

    private void handleTimeEntryEvent(JsonNode data) throws Exception {
        String employeeId = data.path("EmployeeId").asText(null);
        String timeEntryId = data.path("Id").asText(null);
        if (employeeId == null || timeEntryId == null) return;

        double hoursWorked = data.path("HoursWorked").asDouble(0);
        String clockIn = data.path("ClockIn").asText(null);
        if (clockIn == null) return;

        long payPeriod = computePayPeriodFromTimestamp(clockIn);
        String storeKey = employeeId + ":" + payPeriod;

        // O(1) upsert — replaces the ksqlDB COLLECT_LIST + AS_MAP + REDUCE pattern
        hoursStore.computeIfAbsent(storeKey, k -> new ConcurrentHashMap<>())
            .put(timeEntryId, hoursWorked);

        log.info("Time entry updated: employee={}, timeEntry={}, hours={}, period={}",
            employeeId, timeEntryId, hoursWorked, payPeriod);
        computeAndEmit(employeeId, payPeriod);
    }

    private void handleEmployeeDeactivated(JsonNode data) throws Exception {
        String employeeId = data.path("Id").asText(null);
        if (employeeId == null) return;

        deactivatedEmployees.add(employeeId);

        // Find all pay periods for this employee and emit tombstones
        List<String> keysToRemove = new ArrayList<>();
        for (String key : hoursStore.keySet()) {
            if (key.startsWith(employeeId + ":")) {
                keysToRemove.add(key);
            }
        }

        for (String key : keysToRemove) {
            long payPeriodNumber = Long.parseLong(key.substring(key.indexOf(':') + 1));
            String outputKey = mapper.writeValueAsString(
                mapper.createObjectNode()
                    .put("EMPLOYEE_ID", employeeId)
                    .put("PAY_PERIOD_NUMBER", payPeriodNumber)
            );
            context.forward(new Record<>(outputKey, null, System.currentTimeMillis()));
            hoursStore.remove(key);
        }

        // Also check for salaried employees with no time entries but with employee info
        // (they may have had a computed gross pay from payPeriodHours)
        if (keysToRemove.isEmpty()) {
            long currentPeriod = getCurrentPayPeriod();
            String outputKey = mapper.writeValueAsString(
                mapper.createObjectNode()
                    .put("EMPLOYEE_ID", employeeId)
                    .put("PAY_PERIOD_NUMBER", currentPeriod)
            );
            context.forward(new Record<>(outputKey, null, System.currentTimeMillis()));
        }

        employeeInfoStore.remove(employeeId);
        taxConfigStore.remove(employeeId);
        deductionStore.remove(employeeId);

        log.info("Employee deactivated: employee={}, tombstones emitted for {} pay periods",
            employeeId, Math.max(keysToRemove.size(), 1));
    }

    private void handleTaxInfoEvent(JsonNode data) throws Exception {
        String employeeId = data.path("EmployeeId").asText(null);
        if (employeeId == null) return;

        TaxConfig tc = new TaxConfig(
            employeeId,
            data.path("FederalFilingStatus").asText("Single"),
            data.path("State").asText(""),
            data.path("AdditionalFederalWithholding").asDouble(0),
            data.path("AdditionalStateWithholding").asDouble(0)
        );

        taxConfigStore.put(employeeId, mapper.writeValueAsString(tc));
        log.info("Tax config updated: employee={}, filing={}, state={}", employeeId, tc.getFederalFilingStatus(), tc.getState());

        recomputeCurrentPeriod(employeeId);
    }

    private void handleDeductionEvent(JsonNode data, String eventType) throws Exception {
        String employeeId = data.path("EmployeeId").asText(null);
        String deductionId = data.path("Id").asText(null);
        if (employeeId == null || deductionId == null) return;

        DeductionMap dm;
        String existing = deductionStore.get(employeeId);
        if (existing != null) {
            dm = mapper.readValue(existing, DeductionMap.class);
        } else {
            dm = new DeductionMap(employeeId);
        }

        if ("deduction.deactivated".equals(eventType)) {
            dm.putDeduction(deductionId,
                dm.getDeductions().containsKey(deductionId)
                    ? dm.getDeductions().get(deductionId).getAmount() : 0,
                dm.getDeductions().containsKey(deductionId)
                    && dm.getDeductions().get(deductionId).isPercentage(),
                false);
        } else {
            dm.putDeduction(deductionId,
                data.path("Amount").asDouble(0),
                data.path("IsPercentage").asBoolean(false),
                data.path("IsActive").asBoolean(true));
        }

        deductionStore.put(employeeId, mapper.writeValueAsString(dm));
        log.info("Deduction updated: employee={}, deduction={}, event={}", employeeId, deductionId, eventType);

        recomputeCurrentPeriod(employeeId);
    }

    /**
     * Recompute net pay for the current pay period if we have employee info.
     * Used by tax and deduction handlers where there's no specific pay period in the event.
     */
    private void recomputeCurrentPeriod(String employeeId) throws Exception {
        long currentPeriod = getCurrentPayPeriod();
        // Only recompute if we have data for this employee
        if (employeeInfoStore.containsKey(employeeId)) {
            computeAndEmit(employeeId, currentPeriod);
        }
    }

    private void computeAndEmit(String employeeId, long payPeriodNumber) throws Exception {
        if (deactivatedEmployees.contains(employeeId)) {
            String outputKey = mapper.writeValueAsString(
                mapper.createObjectNode()
                    .put("EMPLOYEE_ID", employeeId)
                    .put("PAY_PERIOD_NUMBER", payPeriodNumber)
            );
            context.forward(new Record<>(outputKey, null, System.currentTimeMillis()));
            log.info("Skipped (deactivated): employee={}, period={}, tombstone emitted", employeeId, payPeriodNumber);
            return;
        }

        // Load employee info for gross pay calculation
        String infoJson = employeeInfoStore.get(employeeId);
        if (infoJson == null) return; // Can't compute without pay rate info

        EmployeeInfo info = mapper.readValue(infoJson, EmployeeInfo.class);

        // Compute total hours worked
        double totalHoursWorked;
        if ("2".equals(info.getPayType())) {
            // Salary: use payPeriodHours instead of time entries
            totalHoursWorked = info.getPayPeriodHours();
        } else {
            // Hourly: sum hours from the time entry map — O(N) over unique entries only
            String hoursKey = employeeId + ":" + payPeriodNumber;
            ConcurrentHashMap<String, Double> entries = hoursStore.get(hoursKey);
            if (entries == null || entries.isEmpty()) {
                totalHoursWorked = 0;
            } else {
                totalHoursWorked = entries.values().stream().mapToDouble(Double::doubleValue).sum();
            }
        }

        // Compute effective hourly rate and gross pay
        double effectiveHourlyRate;
        if ("2".equals(info.getPayType())) {
            // Salary: annual rate / 2080 (52 weeks x 40 hours)
            effectiveHourlyRate = info.getPayRate() / 2080.0;
        } else {
            effectiveHourlyRate = info.getPayRate();
        }
        double grossPay = effectiveHourlyRate * totalHoursWorked;

        // Compute taxes
        double federalTax = 0, stateTax = 0, addlFederal = 0, addlState = 0;
        String tcJson = taxConfigStore.get(employeeId);
        if (tcJson != null) {
            TaxConfig tc = mapper.readValue(tcJson, TaxConfig.class);
            federalTax = TaxCalculator.computeFederalTax(grossPay, tc.getFederalFilingStatus());
            stateTax = TaxCalculator.computeStateTax(grossPay, tc.getState());
            addlFederal = tc.getAdditionalFederalWithholding();
            addlState = tc.getAdditionalStateWithholding();
        }
        double totalTax = federalTax + stateTax + addlFederal + addlState;

        // Compute deductions
        double fixedDeductions = 0, percentDeductions = 0;
        String dmJson = deductionStore.get(employeeId);
        if (dmJson != null) {
            DeductionMap dm = mapper.readValue(dmJson, DeductionMap.class);
            fixedDeductions = dm.computeFixedTotal();
            percentDeductions = dm.computePercentTotal(grossPay);
        }
        double totalDeductions = fixedDeductions + percentDeductions;
        double netPay = grossPay - totalTax - totalDeductions;

        // Pay period date range
        String payPeriodStart = formatPayPeriodBoundary(payPeriodNumber);
        String payPeriodEnd = formatPayPeriodBoundary(payPeriodNumber + 1);

        // Build result
        NetPayResult result = new NetPayResult();
        result.setGrossPay(roundTwo(grossPay));
        result.setFederalTax(roundTwo(federalTax));
        result.setStateTax(roundTwo(stateTax));
        result.setAdditionalFederalWithholding(roundTwo(addlFederal));
        result.setAdditionalStateWithholding(roundTwo(addlState));
        result.setTotalTax(roundTwo(totalTax));
        result.setTotalFixedDeductions(roundTwo(fixedDeductions));
        result.setTotalPercentDeductions(roundTwo(percentDeductions));
        result.setTotalDeductions(roundTwo(totalDeductions));
        result.setNetPay(roundTwo(netPay));
        result.setPayRate(info.getPayRate());
        result.setPayType(info.getPayType());
        result.setTotalHoursWorked(totalHoursWorked);
        result.setPayPeriodStart(payPeriodStart);
        result.setPayPeriodEnd(payPeriodEnd);
        result.setEmployeeId(employeeId);
        result.setPayPeriodNumber(payPeriodNumber);

        String outputKey = mapper.writeValueAsString(
            mapper.createObjectNode()
                .put("EMPLOYEE_ID", employeeId)
                .put("PAY_PERIOD_NUMBER", payPeriodNumber)
        );
        String outputValue = mapper.writeValueAsString(result);

        context.forward(new Record<>(outputKey, outputValue, System.currentTimeMillis()));
        log.info("Net pay emitted: employee={}, period={}, gross={}, net={}",
            employeeId, payPeriodNumber, grossPay, result.getNetPay());
    }

    /**
     * Parse an ISO timestamp and compute its pay period number.
     * Handles both "2026-03-14T10:00:00" and "2026-03-14T10:00:00Z" formats.
     */
    static long computePayPeriodFromTimestamp(String timestamp) {
        // Trim fractional seconds and trailing Z for consistent parsing
        String clean = timestamp;
        if (clean.contains(".")) {
            clean = clean.substring(0, clean.indexOf('.'));
        }
        if (clean.endsWith("Z")) {
            clean = clean.substring(0, clean.length() - 1);
        }
        long epochMs = LocalDateTime.parse(clean, PAY_PERIOD_FMT)
            .toInstant(ZoneOffset.UTC).toEpochMilli();
        return (epochMs - PAY_PERIOD_EPOCH_MS) / PAY_PERIOD_DURATION_MS;
    }

    static long getCurrentPayPeriod() {
        return (System.currentTimeMillis() - PAY_PERIOD_EPOCH_MS) / PAY_PERIOD_DURATION_MS;
    }

    private static String formatPayPeriodBoundary(long periodNumber) {
        long epochMs = PAY_PERIOD_EPOCH_MS + (periodNumber * PAY_PERIOD_DURATION_MS);
        return LocalDateTime.ofInstant(Instant.ofEpochMilli(epochMs), ZoneOffset.UTC)
            .format(PAY_PERIOD_FMT);
    }

    private static double roundTwo(double value) {
        return Math.round(value * 100.0) / 100.0;
    }
}
