package com.payroll.netpay;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.payroll.netpay.model.DeductionMap;
import com.payroll.netpay.model.EmployeeInfo;
import com.payroll.netpay.model.TaxConfig;
import org.apache.kafka.clients.admin.AdminClient;
import org.apache.kafka.clients.admin.AdminClientConfig;
import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.apache.kafka.clients.consumer.ConsumerRecords;
import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.common.serialization.StringDeserializer;
import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.StreamsConfig;
import org.apache.kafka.streams.Topology;
import org.apache.kafka.streams.errors.StreamsUncaughtExceptionHandler;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import org.apache.kafka.clients.producer.KafkaProducer;
import org.apache.kafka.clients.producer.ProducerConfig;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.common.serialization.StringSerializer;

import java.time.Duration;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Properties;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.stream.Collectors;

public class NetPayApp {

    private static final Logger log = LoggerFactory.getLogger(NetPayApp.class);
    private static final long RESTART_DELAY_MS = 30_000;

    static final String EMPLOYEE_EVENTS_TOPIC = "employee-events";
    static final String NET_PAY_TOPIC = "employee-net-pay";

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
            log.info("Restarting Net Pay Processor...");
        }

        log.info("Net Pay Processor exited");
    }

    /**
     * Runs a single lifecycle of the Kafka Streams app.
     * @return true if the app should restart (error), false for graceful shutdown.
     */
    private static boolean runOnce() {
        // Clear stale in-memory state from any previous run
        NetPayProcessor.employeeInfoStore.clear();
        NetPayProcessor.hoursStore.clear();
        NetPayProcessor.taxConfigStore.clear();
        NetPayProcessor.deductionStore.clear();
        NetPayProcessor.deactivatedEmployees.clear();

        Properties props = buildConfig();
        String appId = props.getProperty(StreamsConfig.APPLICATION_ID_CONFIG);
        String bootstrapServers = props.getProperty(StreamsConfig.BOOTSTRAP_SERVERS_CONFIG);

        resetConsumerGroup(appId, bootstrapServers);
        prescanEmployeeEvents(bootstrapServers);
        purgeDeactivatedFromNetPay(bootstrapServers);

        Topology topology = buildTopology();
        log.info("Topology:\n{}", topology.describe());

        KafkaStreams streams = new KafkaStreams(topology, props);
        streams.cleanUp();

        CountDownLatch latch = new CountDownLatch(1);

        streams.setUncaughtExceptionHandler(exception -> {
            log.error("Uncaught exception in Kafka Streams: {} - {}",
                exception.getClass().getSimpleName(), exception.getMessage());
            return StreamsUncaughtExceptionHandler.StreamThreadExceptionResponse.SHUTDOWN_CLIENT;
        });

        streams.setStateListener((newState, oldState) -> {
            log.info("Kafka Streams state change: {} -> {}", oldState, newState);
            if (newState == KafkaStreams.State.ERROR) {
                latch.countDown();
            }
        });

        // Ensure the shutdown hook can stop this run's streams instance
        Thread shutdownWatcher = new Thread(() -> {
            while (!shuttingDown) {
                try {
                    Thread.sleep(500);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    return;
                }
            }
            log.info("Shutting down Kafka Streams...");
            streams.close(Duration.ofSeconds(10));
            latch.countDown();
        });
        shutdownWatcher.setDaemon(true);
        shutdownWatcher.start();

        try {
            streams.start();
            log.info("Net Pay Processor started");
            latch.await();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }

        streams.close(Duration.ofSeconds(10));

        if (shuttingDown) {
            return false;
        }

        log.info("Kafka Streams terminated unexpectedly, will attempt auto-recovery");
        return true;
    }

    private static void resetConsumerGroup(String appId, String bootstrapServers) {
        Properties adminProps = new Properties();
        adminProps.put(AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        try (AdminClient admin = AdminClient.create(adminProps)) {
            // Retry with backoff — the previous instance's consumer session may not have
            // expired yet on the broker (default session.timeout.ms = 45s for Kafka Streams).
            for (int attempt = 1; attempt <= 6; attempt++) {
                try {
                    admin.deleteConsumerGroups(Collections.singleton(appId)).all().get();
                    log.info("Deleted consumer group '{}' for full replay", appId);
                    return;
                } catch (Exception e) {
                    String msg = e.getCause() != null ? e.getCause().getMessage() : e.getMessage();
                    if (msg != null && msg.contains("not empty")) {
                        log.info("Consumer group '{}' still has active members, waiting... (attempt {}/6)", appId, attempt);
                        Thread.sleep(10_000);
                    } else if (msg != null && msg.contains("does not exist")) {
                        log.info("Consumer group '{}' does not exist (first run), proceeding", appId);
                        return;
                    } else {
                        log.warn("Failed to delete consumer group '{}': {}", appId, msg);
                        return;
                    }
                }
            }
            log.warn("Could not delete consumer group '{}' after 6 attempts, proceeding anyway", appId);
        } catch (Exception e) {
            log.warn("AdminClient error: {}", e.getMessage());
        }
    }

    /**
     * Pre-scan the employee-events topic from the beginning to build all in-memory state.
     * This ensures correct gross pay and net pay computation from the first event during
     * topology replay, avoiding cross-partition ordering issues where time entry events
     * arrive before employee info events (different CloudEvent IDs = different partitions).
     */
    private static void prescanEmployeeEvents(String bootstrapServers) {
        ObjectMapper mapper = new ObjectMapper();
        Properties props = new Properties();
        props.put(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        props.put(ConsumerConfig.GROUP_ID_CONFIG, "net-pay-prescan-" + System.currentTimeMillis());
        props.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
        props.put(ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, "false");
        props.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());
        props.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());

        try (KafkaConsumer<String, String> consumer = new KafkaConsumer<>(props)) {
            List<TopicPartition> partitions = consumer.partitionsFor(EMPLOYEE_EVENTS_TOPIC)
                .stream()
                .map(pi -> new TopicPartition(pi.topic(), pi.partition()))
                .collect(Collectors.toList());
            consumer.assign(partitions);
            consumer.seekToBeginning(partitions);

            Map<TopicPartition, Long> endOffsets = consumer.endOffsets(partitions);

            int employees = 0, timeEntries = 0, taxConfigs = 0, deductions = 0, deactivated = 0, totalRecords = 0;
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
                    totalRecords++;
                    if (record.value() == null) continue;

                    try {
                        JsonNode envelope = mapper.readTree(record.value());
                        JsonNode data = envelope.path("data");
                        if (data.isMissingNode() || data.isNull()) continue;
                        JsonNode domainEvents = data.path("DomainEvents");
                        if (!domainEvents.isArray() || domainEvents.isEmpty()) continue;
                        String eventType = domainEvents.get(0).path("EventType").asText("");

                        switch (eventType) {
                            case "employee.created":
                            case "employee.updated": {
                                String empId = data.path("Id").asText(null);
                                if (empId == null) break;
                                double payRate = data.path("PayRate").asDouble(0);
                                String payType = String.valueOf(data.path("PayType").asInt(1));
                                double payPeriodHours = data.path("PayPeriodHours").asDouble(40);
                                EmployeeInfo info = new EmployeeInfo(empId, payRate, payType, payPeriodHours);
                                NetPayProcessor.employeeInfoStore.put(empId, mapper.writeValueAsString(info));
                                employees++;
                                break;
                            }
                            case "employee.deactivated": {
                                String empId = data.path("Id").asText(null);
                                if (empId != null) {
                                    NetPayProcessor.deactivatedEmployees.add(empId);
                                    NetPayProcessor.employeeInfoStore.remove(empId);
                                    // Remove hours for deactivated employee
                                    NetPayProcessor.hoursStore.keySet().removeIf(k -> k.startsWith(empId + ":"));
                                    NetPayProcessor.taxConfigStore.remove(empId);
                                    NetPayProcessor.deductionStore.remove(empId);
                                    deactivated++;
                                }
                                break;
                            }
                            case "timeentry.clockedout":
                            case "timeentry.updated": {
                                String empId = data.path("EmployeeId").asText(null);
                                String teId = data.path("Id").asText(null);
                                String clockIn = data.path("ClockIn").asText(null);
                                if (empId == null || teId == null || clockIn == null) break;
                                double hours = data.path("HoursWorked").asDouble(0);
                                long period = NetPayProcessor.computePayPeriodFromTimestamp(clockIn);
                                String key = empId + ":" + period;
                                NetPayProcessor.hoursStore.computeIfAbsent(key, k -> new ConcurrentHashMap<>())
                                    .put(teId, hours);
                                timeEntries++;
                                break;
                            }
                            default: {
                                if (eventType.startsWith("taxinfo.")) {
                                    String empId = data.path("EmployeeId").asText(null);
                                    if (empId == null) break;
                                    TaxConfig tc = new TaxConfig(
                                        empId,
                                        data.path("FederalFilingStatus").asText("Single"),
                                        data.path("State").asText(""),
                                        data.path("AdditionalFederalWithholding").asDouble(0),
                                        data.path("AdditionalStateWithholding").asDouble(0)
                                    );
                                    NetPayProcessor.taxConfigStore.put(empId, mapper.writeValueAsString(tc));
                                    taxConfigs++;
                                } else if (eventType.startsWith("deduction.")) {
                                    String empId = data.path("EmployeeId").asText(null);
                                    String dedId = data.path("Id").asText(null);
                                    if (empId == null || dedId == null) break;
                                    DeductionMap dm;
                                    String existing = NetPayProcessor.deductionStore.get(empId);
                                    if (existing != null) {
                                        dm = mapper.readValue(existing, DeductionMap.class);
                                    } else {
                                        dm = new DeductionMap(empId);
                                    }
                                    if ("deduction.deactivated".equals(eventType)) {
                                        dm.putDeduction(dedId,
                                            dm.getDeductions().containsKey(dedId)
                                                ? dm.getDeductions().get(dedId).getAmount() : 0,
                                            dm.getDeductions().containsKey(dedId)
                                                && dm.getDeductions().get(dedId).isPercentage(),
                                            false);
                                    } else {
                                        dm.putDeduction(dedId,
                                            data.path("Amount").asDouble(0),
                                            data.path("IsPercentage").asBoolean(false),
                                            data.path("IsActive").asBoolean(true));
                                    }
                                    NetPayProcessor.deductionStore.put(empId, mapper.writeValueAsString(dm));
                                    deductions++;
                                }
                                break;
                            }
                        }
                    } catch (Exception e) {
                        // Skip unparseable records
                    }
                }

                // Check if we've caught up
                done = true;
                for (TopicPartition tp : partitions) {
                    if (consumer.position(tp) < endOffsets.get(tp)) {
                        done = false;
                        break;
                    }
                }
            }

            log.info("Pre-scan complete: {} records scanned, {} employee info, {} time entries, {} tax configs, {} deductions, {} deactivated",
                totalRecords, employees, timeEntries, taxConfigs, deductions, deactivated);
        } catch (Exception e) {
            log.warn("Pre-scan failed (will rely on runtime state building): {}", e.getMessage());
        }
    }

    /**
     * Scan the employee-net-pay topic and produce tombstones for any records belonging
     * to deactivated employees. This ensures the ksqlDB SOURCE TABLE drops stale rows.
     */
    private static void purgeDeactivatedFromNetPay(String bootstrapServers) {
        if (NetPayProcessor.deactivatedEmployees.isEmpty()) {
            log.info("No deactivated employees to purge from {}", NET_PAY_TOPIC);
            return;
        }

        ObjectMapper mapper = new ObjectMapper();
        Properties consumerProps = new Properties();
        consumerProps.put(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        consumerProps.put(ConsumerConfig.GROUP_ID_CONFIG, "net-pay-purge-" + System.currentTimeMillis());
        consumerProps.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
        consumerProps.put(ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, "false");
        consumerProps.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());
        consumerProps.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());

        Properties producerProps = new Properties();
        producerProps.put(ProducerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        producerProps.put(ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG, StringSerializer.class.getName());
        producerProps.put(ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG, StringSerializer.class.getName());

        try (KafkaConsumer<String, String> consumer = new KafkaConsumer<>(consumerProps);
             KafkaProducer<String, String> producer = new KafkaProducer<>(producerProps)) {

            List<TopicPartition> partitions = consumer.partitionsFor(NET_PAY_TOPIC)
                .stream()
                .map(pi -> new TopicPartition(pi.topic(), pi.partition()))
                .collect(Collectors.toList());
            consumer.assign(partitions);
            consumer.seekToBeginning(partitions);

            Map<TopicPartition, Long> endOffsets = consumer.endOffsets(partitions);
            Set<String> tombstoneKeys = new HashSet<>();
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
                    if (record.key() == null) continue;
                    try {
                        JsonNode keyNode = mapper.readTree(record.key());
                        String employeeId = keyNode.path("EMPLOYEE_ID").asText(null);
                        if (employeeId != null && NetPayProcessor.deactivatedEmployees.contains(employeeId)) {
                            tombstoneKeys.add(record.key());
                        }
                    } catch (Exception e) {
                        // Skip unparseable keys
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

            // Produce tombstones
            for (String key : tombstoneKeys) {
                producer.send(new ProducerRecord<>(NET_PAY_TOPIC, key, null));
            }
            producer.flush();

            log.info("Purge complete: {} tombstones produced for deactivated employees on {}",
                tombstoneKeys.size(), NET_PAY_TOPIC);
        } catch (Exception e) {
            log.warn("Purge failed (stale records may remain): {}", e.getMessage());
        }
    }

    static Topology buildTopology() {
        Topology topology = new Topology();

        // Single source — all event types come from employee-events
        topology.addSource("employee-events-source",
            Serdes.String().deserializer(), Serdes.String().deserializer(),
            EMPLOYEE_EVENTS_TOPIC);

        // Single processor handles employee info, time entries, tax, deductions, and gross pay
        topology.addProcessor("net-pay-processor",
            NetPayProcessor::new,
            "employee-events-source");

        // Sink
        topology.addSink("net-pay-sink",
            NET_PAY_TOPIC,
            Serdes.String().serializer(), Serdes.String().serializer(),
            "net-pay-processor");

        return topology;
    }

    private static Properties buildConfig() {
        Properties props = new Properties();
        props.put(StreamsConfig.APPLICATION_ID_CONFIG,
            envOrDefault("APPLICATION_ID", "net-pay-processor"));
        props.put(StreamsConfig.BOOTSTRAP_SERVERS_CONFIG,
            envOrDefault("KAFKA_BOOTSTRAP_SERVERS", "localhost:29092"));
        props.put(StreamsConfig.DEFAULT_KEY_SERDE_CLASS_CONFIG,
            Serdes.StringSerde.class.getName());
        props.put(StreamsConfig.DEFAULT_VALUE_SERDE_CLASS_CONFIG,
            Serdes.StringSerde.class.getName());
        // Process one record at a time for consistency
        props.put(StreamsConfig.NUM_STREAM_THREADS_CONFIG, 1);
        // Commit interval — 1 second for near-real-time
        props.put(StreamsConfig.COMMIT_INTERVAL_MS_CONFIG, 1000);
        // Start from earliest on fresh start — rebuilds in-memory state from full history
        props.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
        return props;
    }

    private static String envOrDefault(String key, String defaultValue) {
        String value = System.getenv(key);
        return value != null ? value : defaultValue;
    }
}
