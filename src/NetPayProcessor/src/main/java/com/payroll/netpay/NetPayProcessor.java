package com.payroll.netpay;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.payroll.netpay.model.DeductionMap;
import com.payroll.netpay.model.GrossPay;
import com.payroll.netpay.model.NetPayResult;
import com.payroll.netpay.model.TaxConfig;
import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.Record;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Unified processor that handles both gross-pay and employee-events sources.
 *
 * State is kept in static ConcurrentHashMaps shared across all processor instances
 * rather than Kafka Streams partitioned state stores. This avoids the co-partitioning
 * problem: employee-gross-pay and employee-events have different key schemas, so the
 * same employee's data lands in different partitions/tasks, making per-task state stores
 * invisible across sources. Shared in-memory maps ensure all processors see the same
 * state regardless of partition assignment. Safe for this single-instance, single-thread POC.
 */
public class NetPayProcessor implements Processor<String, String, String, String> {

    private static final Logger log = LoggerFactory.getLogger(NetPayProcessor.class);
    private static final ObjectMapper mapper = new ObjectMapper();

    // Pay period epoch: 2024-01-01T00:00:00Z in millis
    private static final long PAY_PERIOD_EPOCH_MS = 1704067200000L;
    private static final long PAY_PERIOD_DURATION_MS = 14L * 24 * 60 * 60 * 1000; // 14 days

    // Shared in-memory state — keyed by employeeId:payPeriod for gross pay, employeeId for others
    static final ConcurrentHashMap<String, String> grossPayStore = new ConcurrentHashMap<>();
    static final ConcurrentHashMap<String, String> taxConfigStore = new ConcurrentHashMap<>();
    static final ConcurrentHashMap<String, String> deductionStore = new ConcurrentHashMap<>();
    // Tracks deactivated employees so late-arriving gross pay events emit tombstones instead of data.
    // Handles the replay race condition where employee-gross-pay events arrive after the deactivation.
    // Cleared when an employee.created event re-uses the same ID (won't happen with GUIDs, but safe).
    static final Set<String> deactivatedEmployees = Collections.newSetFromMap(new ConcurrentHashMap<>());

    private final String sourceName;
    private ProcessorContext<String, String> context;

    /**
     * @param sourceName identifies which source topic this processor instance handles:
     *                   "gross-pay" or "employee-events"
     */
    public NetPayProcessor(String sourceName) {
        this.sourceName = sourceName;
    }

    @Override
    public void init(ProcessorContext<String, String> context) {
        this.context = context;
    }

    @Override
    public void process(Record<String, String> record) {
        if (record.value() == null) return;

        try {
            if ("gross-pay".equals(sourceName)) {
                handleGrossPay(record);
            } else {
                handleEmployeeEvent(record);
            }
        } catch (Exception e) {
            log.error("Error processing record from {}: {}", sourceName, e.getMessage(), e);
        }
    }

    private void handleGrossPay(Record<String, String> record) throws Exception {
        JsonNode keyNode = mapper.readTree(record.key());
        JsonNode valueNode = mapper.readTree(record.value());

        String employeeId = keyNode.get("EMPLOYEE_ID").asText();
        long payPeriodNumber = keyNode.get("PAY_PERIOD_NUMBER").asLong();

        // If this employee was deactivated, emit a tombstone instead of net pay
        if (deactivatedEmployees.contains(employeeId)) {
            String outputKey = mapper.writeValueAsString(
                mapper.createObjectNode()
                    .put("employeeId", employeeId)
                    .put("payPeriodNumber", payPeriodNumber)
            );
            context.forward(new Record<>(outputKey, null, System.currentTimeMillis()));
            log.info("Gross pay skipped (deactivated): employee={}, period={}, tombstone emitted", employeeId, payPeriodNumber);
            return;
        }

        GrossPay gp = new GrossPay();
        gp.setEmployeeId(employeeId);
        gp.setPayPeriodNumber(payPeriodNumber);
        gp.setPayRate(valueNode.path("PAY_RATE").asDouble(0));
        gp.setPayType(valueNode.path("PAY_TYPE").asText("1"));
        gp.setGrossPay(valueNode.path("GROSS_PAY").asDouble(0));
        gp.setTotalHoursWorked(valueNode.path("TOTAL_HOURS_WORKED").asDouble(0));
        gp.setPayPeriodStart(valueNode.path("PAY_PERIOD_START").asText(""));
        gp.setPayPeriodEnd(valueNode.path("PAY_PERIOD_END").asText(""));

        String storeKey = employeeId + ":" + payPeriodNumber;
        grossPayStore.put(storeKey, mapper.writeValueAsString(gp));

        log.info("Gross pay updated: employee={}, period={}, gross={}", employeeId, payPeriodNumber, gp.getGrossPay());
        computeAndEmit(employeeId, payPeriodNumber);
    }

    private void handleEmployeeEvent(Record<String, String> record) throws Exception {
        JsonNode envelope = mapper.readTree(record.value());

        // Dapr CloudEvent: data is a stringified JSON
        String dataStr = envelope.path("data").asText(null);
        if (dataStr == null) {
            // data might be an object (non-Dapr path)
            JsonNode dataNode = envelope.path("data");
            if (dataNode.isMissingNode() || dataNode.isNull()) return;
            dataStr = dataNode.toString();
        }

        JsonNode data = mapper.readTree(dataStr);

        // Extract event type from DomainEvents[0].EventType
        JsonNode domainEvents = data.path("DomainEvents");
        if (!domainEvents.isArray() || domainEvents.isEmpty()) return;
        String eventType = domainEvents.get(0).path("EventType").asText("");

        if ("employee.created".equals(eventType)) {
            // No-op here. The pre-scan already built the correct deactivated set from
            // the full event history. Removing from the set during topology replay would
            // undo the pre-scan's work (old employee.created events arrive before their
            // deactivation events due to cross-partition ordering). New employees use
            // fresh GUIDs and are never in the deactivated set, so no removal needed.
        } else if ("employee.deactivated".equals(eventType)) {
            handleEmployeeDeactivated(data);
        } else if (eventType.startsWith("taxinfo.")) {
            handleTaxInfoEvent(data);
        } else if (eventType.startsWith("deduction.")) {
            handleDeductionEvent(data, eventType);
        }
        // employee.created/updated and timeentry.* are handled by gross pay
    }

    private void handleEmployeeDeactivated(JsonNode data) throws Exception {
        String employeeId = data.path("Id").asText(null);
        if (employeeId == null) return;

        // Mark as deactivated so late-arriving gross pay events also emit tombstones
        deactivatedEmployees.add(employeeId);

        // Find all pay periods for this employee in the gross pay store
        List<String> keysToRemove = new ArrayList<>();
        for (String key : grossPayStore.keySet()) {
            if (key.startsWith(employeeId + ":")) {
                keysToRemove.add(key);
            }
        }

        // Emit tombstones (null value) for each pay period — removes rows from ksqlDB tables
        for (String key : keysToRemove) {
            long payPeriodNumber = Long.parseLong(key.substring(key.indexOf(':') + 1));
            String outputKey = mapper.writeValueAsString(
                mapper.createObjectNode()
                    .put("employeeId", employeeId)
                    .put("payPeriodNumber", payPeriodNumber)
            );
            context.forward(new Record<>(outputKey, null, System.currentTimeMillis()));
            grossPayStore.remove(key);
        }

        // Clean up other stores
        taxConfigStore.remove(employeeId);
        deductionStore.remove(employeeId);

        log.info("Employee deactivated: employee={}, tombstones emitted for {} pay periods",
            employeeId, keysToRemove.size());
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

        // Recompute for current pay period
        long currentPeriod = getCurrentPayPeriod();
        String storeKey = employeeId + ":" + currentPeriod;
        if (grossPayStore.containsKey(storeKey)) {
            computeAndEmit(employeeId, currentPeriod);
        }
    }

    private void handleDeductionEvent(JsonNode data, String eventType) throws Exception {
        String employeeId = data.path("EmployeeId").asText(null);
        String deductionId = data.path("Id").asText(null);
        if (employeeId == null || deductionId == null) return;

        // Load existing deduction map or create new
        DeductionMap dm;
        String existing = deductionStore.get(employeeId);
        if (existing != null) {
            dm = mapper.readValue(existing, DeductionMap.class);
        } else {
            dm = new DeductionMap(employeeId);
        }

        if ("deduction.deactivated".equals(eventType)) {
            // Mark as inactive but keep in map
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

        // Recompute for current pay period
        long currentPeriod = getCurrentPayPeriod();
        String storeKey = employeeId + ":" + currentPeriod;
        if (grossPayStore.containsKey(storeKey)) {
            computeAndEmit(employeeId, currentPeriod);
        }
    }

    private void computeAndEmit(String employeeId, long payPeriodNumber) throws Exception {
        String storeKey = employeeId + ":" + payPeriodNumber;
        String gpJson = grossPayStore.get(storeKey);
        if (gpJson == null) return;

        GrossPay gp = mapper.readValue(gpJson, GrossPay.class);
        double grossPay = gp.getGrossPay();

        // Load tax config (may not exist yet)
        double federalTax = 0;
        double stateTax = 0;
        double addlFederal = 0;
        double addlState = 0;
        String tcJson = taxConfigStore.get(employeeId);
        if (tcJson != null) {
            TaxConfig tc = mapper.readValue(tcJson, TaxConfig.class);
            federalTax = TaxCalculator.computeFederalTax(grossPay, tc.getFederalFilingStatus());
            stateTax = TaxCalculator.computeStateTax(grossPay, tc.getState());
            addlFederal = tc.getAdditionalFederalWithholding();
            addlState = tc.getAdditionalStateWithholding();
        }

        double totalTax = federalTax + stateTax + addlFederal + addlState;

        // Load deductions
        double fixedDeductions = 0;
        double percentDeductions = 0;
        String dmJson = deductionStore.get(employeeId);
        if (dmJson != null) {
            DeductionMap dm = mapper.readValue(dmJson, DeductionMap.class);
            fixedDeductions = dm.computeFixedTotal();
            percentDeductions = dm.computePercentTotal(grossPay);
        }
        double totalDeductions = fixedDeductions + percentDeductions;

        double netPay = grossPay - totalTax - totalDeductions;

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
        result.setPayRate(gp.getPayRate());
        result.setPayType(gp.getPayType());
        result.setTotalHoursWorked(gp.getTotalHoursWorked());
        result.setPayPeriodStart(gp.getPayPeriodStart());
        result.setPayPeriodEnd(gp.getPayPeriodEnd());
        result.setEmployeeId(employeeId);
        result.setPayPeriodNumber(payPeriodNumber);

        // Output key: {"employeeId":"...","payPeriodNumber":55}
        String outputKey = mapper.writeValueAsString(
            mapper.createObjectNode()
                .put("employeeId", employeeId)
                .put("payPeriodNumber", payPeriodNumber)
        );
        String outputValue = mapper.writeValueAsString(result);

        context.forward(new Record<>(outputKey, outputValue, System.currentTimeMillis()));
        log.info("Net pay emitted: employee={}, period={}, gross={}, net={}",
            employeeId, payPeriodNumber, grossPay, result.getNetPay());
    }

    static long getCurrentPayPeriod() {
        return (System.currentTimeMillis() - PAY_PERIOD_EPOCH_MS) / PAY_PERIOD_DURATION_MS;
    }

    private static double roundTwo(double value) {
        return Math.round(value * 100.0) / 100.0;
    }
}
