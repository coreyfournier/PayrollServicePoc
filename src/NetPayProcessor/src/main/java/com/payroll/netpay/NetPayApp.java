package com.payroll.netpay;

import org.apache.kafka.clients.admin.AdminClient;
import org.apache.kafka.clients.admin.AdminClientConfig;
import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.StreamsConfig;
import org.apache.kafka.streams.Topology;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.Collections;
import java.util.Properties;
import java.util.concurrent.CountDownLatch;

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
