package com.payroll.netpay;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
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
import java.util.concurrent.CountDownLatch;
import java.util.stream.Collectors;

public class NetPayApp {

    private static final Logger log = LoggerFactory.getLogger(NetPayApp.class);

    static final String GROSS_PAY_TOPIC = "employee-gross-pay";
    static final String EMPLOYEE_EVENTS_TOPIC = "employee-events";
    static final String NET_PAY_TOPIC = "employee-net-pay";

    public static void main(String[] args) {
        Properties props = buildConfig();
        String appId = props.getProperty(StreamsConfig.APPLICATION_ID_CONFIG);
        String bootstrapServers = props.getProperty(StreamsConfig.BOOTSTRAP_SERVERS_CONFIG);

        // Delete the consumer group so Kafka Streams replays from earliest offset,
        // rebuilding the in-memory ConcurrentHashMap state from the full event history.
        // Required because state lives in static maps (not Kafka Streams state stores)
        // and is lost on restart.
        resetConsumerGroup(appId, bootstrapServers);

        // Pre-scan employee-events to build the deactivatedEmployees set BEFORE starting
        // the topology. This is necessary because employee-events and employee-gross-pay
        // have different partition keys (Dapr state key vs ksqlDB aggregate key), so the
        // same employee's events land in different Kafka Streams tasks. Without pre-scanning,
        // gross pay events can be processed before deactivation events, causing deactivated
        // employees to appear in the output.
        prescanEmployeeEvents(bootstrapServers);

        // Purge stale records from the employee-net-pay topic by producing tombstones
        // for any deactivated employees. The ksqlDB SOURCE TABLE only removes rows when
        // it receives tombstones; kafka-delete-records alone is insufficient.
        purgeDeactivatedFromNetPay(bootstrapServers);

        Topology topology = buildTopology();
        log.info("Topology:\n{}", topology.describe());

        KafkaStreams streams = new KafkaStreams(topology, props);
        streams.cleanUp();

        CountDownLatch latch = new CountDownLatch(1);
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            log.info("Shutting down...");
            streams.close();
            latch.countDown();
        }));

        try {
            streams.start();
            log.info("Net Pay Processor started");
            latch.await();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
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
     * Pre-scan the employee-events topic from the beginning to build the deactivatedEmployees set.
     * This ensures all deactivated employees are known before any gross pay events are processed,
     * avoiding the cross-partition ordering problem between the two source topics.
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
            // Manually assign all partitions and seek to beginning
            List<TopicPartition> partitions = consumer.partitionsFor(EMPLOYEE_EVENTS_TOPIC)
                .stream()
                .map(pi -> new TopicPartition(pi.topic(), pi.partition()))
                .collect(Collectors.toList());
            consumer.assign(partitions);
            consumer.seekToBeginning(partitions);

            // Get end offsets to know when we've caught up
            Map<TopicPartition, Long> endOffsets = consumer.endOffsets(partitions);

            int created = 0, deactivated = 0, totalRecords = 0;
            boolean done = false;

            while (!done) {
                ConsumerRecords<String, String> records = consumer.poll(Duration.ofSeconds(5));
                if (records.isEmpty()) {
                    // Check if we've reached the end of all partitions
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
                        String dataStr = envelope.path("data").asText(null);
                        if (dataStr == null) {
                            JsonNode dataNode = envelope.path("data");
                            if (dataNode.isMissingNode() || dataNode.isNull()) continue;
                            dataStr = dataNode.toString();
                        }

                        JsonNode data = mapper.readTree(dataStr);
                        JsonNode domainEvents = data.path("DomainEvents");
                        if (!domainEvents.isArray() || domainEvents.isEmpty()) continue;
                        String eventType = domainEvents.get(0).path("EventType").asText("");

                        if ("employee.created".equals(eventType)) {
                            created++;
                            // Don't remove from deactivated set here. Dapr outbox assigns
                            // different CloudEvent IDs per write, so events for the same
                            // employee land on different partitions. When polling, a created
                            // event from one partition may arrive AFTER a deactivated event
                            // from another partition (despite being chronologically earlier),
                            // incorrectly undoing the deactivation. With GUIDs, employee IDs
                            // are never reused, so only tracking deactivations is sufficient.
                        } else if ("employee.deactivated".equals(eventType)) {
                            String empId = data.path("Id").asText(null);
                            if (empId != null) {
                                NetPayProcessor.deactivatedEmployees.add(empId);
                                deactivated++;
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

            log.info("Pre-scan complete: {} records scanned, {} created, {} deactivated, {} employees in deactivated set",
                totalRecords, created, deactivated, NetPayProcessor.deactivatedEmployees.size());
        } catch (Exception e) {
            log.warn("Pre-scan failed (will rely on runtime deactivation tracking): {}", e.getMessage());
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
                        String employeeId = keyNode.path("employeeId").asText(null);
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

        // Sources
        topology.addSource("gross-pay-source",
            Serdes.String().deserializer(), Serdes.String().deserializer(),
            GROSS_PAY_TOPIC);

        topology.addSource("employee-events-source",
            Serdes.String().deserializer(), Serdes.String().deserializer(),
            EMPLOYEE_EVENTS_TOPIC);

        // Processors — each wired to its source
        topology.addProcessor("gross-pay-processor",
            () -> new NetPayProcessor("gross-pay"),
            "gross-pay-source");

        topology.addProcessor("employee-events-processor",
            () -> new NetPayProcessor("employee-events"),
            "employee-events-source");

        // Sink
        topology.addSink("net-pay-sink",
            NET_PAY_TOPIC,
            Serdes.String().serializer(), Serdes.String().serializer(),
            "gross-pay-processor", "employee-events-processor");

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
