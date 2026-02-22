package com.payroll.esupdater;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.apache.kafka.clients.consumer.ConsumerRecords;
import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.clients.producer.KafkaProducer;
import org.apache.kafka.clients.producer.ProducerConfig;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.StringDeserializer;
import org.apache.kafka.common.serialization.StringSerializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.stream.Collectors;

public class ElasticsearchUpdaterApp {

    private static final Logger log = LoggerFactory.getLogger(ElasticsearchUpdaterApp.class);
    private static final ObjectMapper mapper = new ObjectMapper();
    private static final long RESTART_DELAY_MS = 30_000;
    private static final int MAX_PAY_PERIODS = 4;

    static final String EMPLOYEE_INFO_TOPIC = "employee-info";
    static final String EMPLOYEE_NET_PAY_TOPIC = "employee-net-pay";
    static final String EMPLOYEE_SEARCH_TOPIC = "employee-search";

    // In-memory state
    static final Map<String, EmployeeInfo> employeeInfoMap = new ConcurrentHashMap<>();
    static final Map<String, TreeMap<Long, PayPeriodRecord>> payPeriodsMap = new ConcurrentHashMap<>();

    private static volatile boolean shuttingDown = false;

    public static void main(String[] args) {
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            log.info("Shutdown hook fired, signaling graceful shutdown...");
            shuttingDown = true;
        }));

        while (!shuttingDown) {
            boolean shouldRestart = runOnce();
            if (!shouldRestart) {
                break;
            }
            log.info("Will restart in {} seconds...", RESTART_DELAY_MS / 1000);
            try {
                Thread.sleep(RESTART_DELAY_MS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                break;
            }
            if (shuttingDown) {
                break;
            }
            log.info("Restarting Elasticsearch Updater...");
        }

        log.info("Elasticsearch Updater exited");
    }

    private static boolean runOnce() {
        // Clear stale in-memory state from any previous run
        employeeInfoMap.clear();
        payPeriodsMap.clear();

        String bootstrapServers = envOrDefault("KAFKA_BOOTSTRAP_SERVERS", "localhost:29092");
        String groupId = envOrDefault("APPLICATION_ID", "elasticsearch-updater");

        try {
            // Pre-scan both topics to rebuild in-memory state
            prescan(bootstrapServers);

            // Main consumer loop
            Properties consumerProps = new Properties();
            consumerProps.put(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
            consumerProps.put(ConsumerConfig.GROUP_ID_CONFIG, groupId);
            consumerProps.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
            consumerProps.put(ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, "true");
            consumerProps.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());
            consumerProps.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());

            Properties producerProps = new Properties();
            producerProps.put(ProducerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
            producerProps.put(ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG, StringSerializer.class.getName());
            producerProps.put(ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG, StringSerializer.class.getName());

            try (KafkaConsumer<String, String> consumer = new KafkaConsumer<>(consumerProps);
                 KafkaProducer<String, String> producer = new KafkaProducer<>(producerProps)) {

                consumer.subscribe(Arrays.asList(EMPLOYEE_INFO_TOPIC, EMPLOYEE_NET_PAY_TOPIC));
                log.info("Elasticsearch Updater started, subscribed to [{}, {}]",
                    EMPLOYEE_INFO_TOPIC, EMPLOYEE_NET_PAY_TOPIC);

                while (!shuttingDown) {
                    ConsumerRecords<String, String> records = consumer.poll(Duration.ofSeconds(1));
                    for (ConsumerRecord<String, String> record : records) {
                        try {
                            if (EMPLOYEE_INFO_TOPIC.equals(record.topic())) {
                                handleEmployeeInfo(record, producer);
                            } else if (EMPLOYEE_NET_PAY_TOPIC.equals(record.topic())) {
                                handleNetPay(record, producer);
                            }
                        } catch (Exception e) {
                            log.error("Error processing record from {}: {}", record.topic(), e.getMessage(), e);
                        }
                    }
                }
            }

            return false; // graceful shutdown
        } catch (Exception e) {
            log.error("Elasticsearch Updater failed: {}", e.getMessage(), e);
            return true; // restart
        }
    }

    /**
     * Pre-scan both topics from the beginning to rebuild in-memory state.
     * Uses a temporary consumer group with manual partition assignment.
     */
    private static void prescan(String bootstrapServers) {
        log.info("Pre-scanning topics to rebuild in-memory state...");

        Properties props = new Properties();
        props.put(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        props.put(ConsumerConfig.GROUP_ID_CONFIG, "es-updater-prescan-" + System.currentTimeMillis());
        props.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
        props.put(ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, "false");
        props.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());
        props.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());

        try (KafkaConsumer<String, String> consumer = new KafkaConsumer<>(props)) {
            // Assign all partitions from both topics
            List<TopicPartition> partitions = new ArrayList<>();
            for (String topic : Arrays.asList(EMPLOYEE_INFO_TOPIC, EMPLOYEE_NET_PAY_TOPIC)) {
                try {
                    partitions.addAll(
                        consumer.partitionsFor(topic).stream()
                            .map(pi -> new TopicPartition(pi.topic(), pi.partition()))
                            .collect(Collectors.toList())
                    );
                } catch (Exception e) {
                    log.warn("Topic {} not available for pre-scan: {}", topic, e.getMessage());
                }
            }

            if (partitions.isEmpty()) {
                log.info("No partitions available for pre-scan, starting fresh");
                return;
            }

            consumer.assign(partitions);
            consumer.seekToBeginning(partitions);

            Map<TopicPartition, Long> endOffsets = consumer.endOffsets(partitions);
            int infoCount = 0, netPayCount = 0;
            boolean done = false;

            while (!done) {
                ConsumerRecords<String, String> records = consumer.poll(Duration.ofSeconds(5));
                if (records.isEmpty()) {
                    done = true;
                    for (TopicPartition tp : partitions) {
                        if (consumer.position(tp) < endOffsets.get(tp)) {
                            done = false;
                            break;
                        }
                    }
                    continue;
                }

                for (ConsumerRecord<String, String> record : records) {
                    try {
                        if (EMPLOYEE_INFO_TOPIC.equals(record.topic())) {
                            processEmployeeInfoRecord(record);
                            infoCount++;
                        } else if (EMPLOYEE_NET_PAY_TOPIC.equals(record.topic())) {
                            processNetPayRecord(record);
                            netPayCount++;
                        }
                    } catch (Exception e) {
                        // Skip unparseable records during pre-scan
                    }
                }

                done = true;
                for (TopicPartition tp : partitions) {
                    if (consumer.position(tp) < endOffsets.get(tp)) {
                        done = false;
                        break;
                    }
                }
            }

            log.info("Pre-scan complete: {} employee-info records, {} net-pay records, {} employees in state",
                infoCount, netPayCount, employeeInfoMap.size());
        } catch (Exception e) {
            log.warn("Pre-scan failed (starting with empty state): {}", e.getMessage());
        }
    }

    /**
     * Extract employee ID from a record key.
     * ksqlDB KEY_FORMAT='JSON' with a single key column produces a JSON string value
     * (e.g. "d0084eb8-..."), not a JSON object {"EMPLOYEE_ID":"..."}.
     */
    private static String extractEmployeeIdFromKey(String rawKey) {
        if (rawKey == null) return null;
        try {
            JsonNode key = mapper.readTree(rawKey);
            if (key.isTextual()) {
                return key.asText();
            }
            // Fallback: JSON object with EMPLOYEE_ID field
            String id = key.path("EMPLOYEE_ID").asText(null);
            if (id != null) return id;
        } catch (Exception e) {
            // Raw string without JSON wrapping
            return rawKey.replaceAll("^\"|\"$", "");
        }
        return null;
    }

    /**
     * Parse an employee-info record and update the in-memory map.
     * The key from ksqlDB EMPLOYEE_INFO table is a JSON string: "employee-id-guid"
     */
    private static void processEmployeeInfoRecord(ConsumerRecord<String, String> record) throws Exception {
        if (record.value() == null || record.key() == null) return;

        JsonNode value = mapper.readTree(record.value());
        String employeeId = value.path("EMPLOYEE_ID").asText(null);
        if (employeeId == null) {
            employeeId = extractEmployeeIdFromKey(record.key());
        }
        if (employeeId == null) return;

        EmployeeInfo info = new EmployeeInfo();
        info.setEmployeeId(employeeId);
        info.setFirstName(value.path("FIRST_NAME").asText(""));
        info.setLastName(value.path("LAST_NAME").asText(""));
        info.setEmail(value.path("EMAIL").asText(""));
        info.setPayType(value.path("PAY_TYPE").asText(""));
        info.setPayRate(value.path("PAY_RATE").asDouble(0));
        info.setPayPeriodHours(value.path("PAY_PERIOD_HOURS").asDouble(0));
        info.setIsActive(value.path("IS_ACTIVE").asText("true"));
        info.setHireDate(value.path("HIRE_DATE").asText(""));

        employeeInfoMap.put(employeeId, info);
    }

    /**
     * Parse an employee-net-pay record and update the in-memory TreeMap.
     * Key: {"EMPLOYEE_ID":"...","PAY_PERIOD_NUMBER":55}
     */
    private static void processNetPayRecord(ConsumerRecord<String, String> record) throws Exception {
        if (record.key() == null) return;

        JsonNode keyNode = mapper.readTree(record.key());
        String employeeId = keyNode.path("EMPLOYEE_ID").asText(null);
        long payPeriodNumber = keyNode.path("PAY_PERIOD_NUMBER").asLong(-1);
        if (employeeId == null || payPeriodNumber < 0) return;

        TreeMap<Long, PayPeriodRecord> periods = payPeriodsMap.computeIfAbsent(
            employeeId, k -> new TreeMap<>());

        if (record.value() == null) {
            // Tombstone — remove this pay period
            periods.remove(payPeriodNumber);
            return;
        }

        JsonNode value = mapper.readTree(record.value());
        PayPeriodRecord pp = new PayPeriodRecord();
        pp.setPayPeriodNumber(payPeriodNumber);
        pp.setGrossPay(value.path("GROSS_PAY").asDouble(0));
        pp.setFederalTax(value.path("FEDERAL_TAX").asDouble(0));
        pp.setStateTax(value.path("STATE_TAX").asDouble(0));
        pp.setAdditionalFederalWithholding(value.path("ADDITIONAL_FEDERAL_WITHHOLDING").asDouble(0));
        pp.setAdditionalStateWithholding(value.path("ADDITIONAL_STATE_WITHHOLDING").asDouble(0));
        pp.setTotalTax(value.path("TOTAL_TAX").asDouble(0));
        pp.setTotalFixedDeductions(value.path("TOTAL_FIXED_DEDUCTIONS").asDouble(0));
        pp.setTotalPercentDeductions(value.path("TOTAL_PERCENT_DEDUCTIONS").asDouble(0));
        pp.setTotalDeductions(value.path("TOTAL_DEDUCTIONS").asDouble(0));
        pp.setNetPay(value.path("NET_PAY").asDouble(0));
        pp.setPayRate(value.path("PAY_RATE").asDouble(0));
        pp.setPayType(value.path("PAY_TYPE").asText(""));
        pp.setTotalHoursWorked(value.path("TOTAL_HOURS_WORKED").asDouble(0));
        pp.setPayPeriodStart(value.path("PAY_PERIOD_START").asText(""));
        pp.setPayPeriodEnd(value.path("PAY_PERIOD_END").asText(""));

        periods.put(payPeriodNumber, pp);

        // Trim to last N pay periods
        while (periods.size() > MAX_PAY_PERIODS) {
            periods.pollFirstEntry();
        }
    }

    private static void handleEmployeeInfo(ConsumerRecord<String, String> record,
                                            KafkaProducer<String, String> producer) throws Exception {
        processEmployeeInfoRecord(record);

        if (record.key() == null) return;
        String employeeId = extractEmployeeIdFromKey(record.key());
        if (employeeId == null && record.value() != null) {
            JsonNode value = mapper.readTree(record.value());
            employeeId = value.path("EMPLOYEE_ID").asText(null);
        }
        if (employeeId == null) return;

        EmployeeInfo info = employeeInfoMap.get(employeeId);
        if (info != null && "false".equalsIgnoreCase(info.getIsActive())) {
            // Deactivated — produce tombstone
            producer.send(new ProducerRecord<>(EMPLOYEE_SEARCH_TOPIC, employeeId, null));
            producer.flush();
            log.info("Employee deactivated, tombstone sent: {}", employeeId);
            return;
        }

        produceSearchDocument(employeeId, producer);
    }

    private static void handleNetPay(ConsumerRecord<String, String> record,
                                      KafkaProducer<String, String> producer) throws Exception {
        if (record.key() == null) return;

        JsonNode keyNode = mapper.readTree(record.key());
        String employeeId = keyNode.path("EMPLOYEE_ID").asText(null);
        if (employeeId == null) return;

        processNetPayRecord(record);
        produceSearchDocument(employeeId, producer);
    }

    /**
     * Build combined document from in-memory state and produce to employee-search topic.
     */
    private static void produceSearchDocument(String employeeId,
                                               KafkaProducer<String, String> producer) throws Exception {
        EmployeeInfo info = employeeInfoMap.get(employeeId);
        if (info == null) {
            // No employee info yet — skip until we have both pieces
            log.debug("No employee info for {}, skipping search document", employeeId);
            return;
        }

        EmployeeSearchDocument doc = new EmployeeSearchDocument();
        doc.setEmployeeId(employeeId);
        doc.setFirstName(info.getFirstName());
        doc.setLastName(info.getLastName());
        doc.setEmail(info.getEmail());
        doc.setPayType(info.getPayType());
        doc.setPayRate(info.getPayRate());
        doc.setPayPeriodHours(info.getPayPeriodHours());
        doc.setActive(!"false".equalsIgnoreCase(info.getIsActive()));
        doc.setHireDate(info.getHireDate());

        TreeMap<Long, PayPeriodRecord> periods = payPeriodsMap.get(employeeId);
        if (periods != null && !periods.isEmpty()) {
            doc.setPayPeriods(new ArrayList<>(periods.values()));
        }

        String value = mapper.writeValueAsString(doc);
        producer.send(new ProducerRecord<>(EMPLOYEE_SEARCH_TOPIC, employeeId, value));
        producer.flush();

        log.info("Search document produced: employee={}, periods={}",
            employeeId, doc.getPayPeriods().size());
    }

    private static String envOrDefault(String key, String defaultValue) {
        String value = System.getenv(key);
        return value != null ? value : defaultValue;
    }

    /**
     * Simple POJO for employee info from the employee-info topic.
     */
    static class EmployeeInfo {
        private String employeeId;
        private String firstName;
        private String lastName;
        private String email;
        private String payType;
        private double payRate;
        private double payPeriodHours;
        private String isActive;
        private String hireDate;

        public String getEmployeeId() { return employeeId; }
        public void setEmployeeId(String employeeId) { this.employeeId = employeeId; }
        public String getFirstName() { return firstName; }
        public void setFirstName(String firstName) { this.firstName = firstName; }
        public String getLastName() { return lastName; }
        public void setLastName(String lastName) { this.lastName = lastName; }
        public String getEmail() { return email; }
        public void setEmail(String email) { this.email = email; }
        public String getPayType() { return payType; }
        public void setPayType(String payType) { this.payType = payType; }
        public double getPayRate() { return payRate; }
        public void setPayRate(double payRate) { this.payRate = payRate; }
        public double getPayPeriodHours() { return payPeriodHours; }
        public void setPayPeriodHours(double payPeriodHours) { this.payPeriodHours = payPeriodHours; }
        public String getIsActive() { return isActive; }
        public void setIsActive(String isActive) { this.isActive = isActive; }
        public String getHireDate() { return hireDate; }
        public void setHireDate(String hireDate) { this.hireDate = hireDate; }
    }
}
