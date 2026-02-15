-- ksqlDB statements for Pay Period Hours materialized view
-- Executed by ksqldb-init container on startup
--
-- Aggregates total hours per employee per pay period into the
-- payperiod-hours-changed Kafka topic. Edits are handled without
-- double-counting via AS_MAP keyed by TIME_ENTRY_ID: duplicate keys
-- are overwritten with the latest value, then REDUCE sums the map.
--
-- IMPORTANT: Dapr's transactional outbox serializes the entity (not the domain event)
-- and the data payload arrives as a JSON *string* (not object). See:
-- https://github.com/dapr/dapr/issues/8130
-- Therefore we declare data as VARCHAR and use EXTRACTJSONFIELD to parse fields.
-- Entity field names are PascalCase (Id, EmployeeId, ClockIn, ClockOut, HoursWorked).
-- The event type lives inside the stringified DomainEvents array.

-- ============================================================
-- Drop existing objects (reverse dependency order)
-- Covers both old names and new names for idempotent re-runs
-- ============================================================

-- Current / new objects
DROP TABLE IF EXISTS PAY_PERIOD_HOURS DELETE TOPIC;
DROP STREAM IF EXISTS TIME_ENTRY_EVENTS DELETE TOPIC;

-- Legacy objects from previous schema versions
DROP TABLE IF EXISTS TIME_ENTRY_LATEST_HOURS DELETE TOPIC;
DROP STREAM IF EXISTS CLOCKOUT_EVENTS DELETE TOPIC;

-- Base stream last (no DELETE TOPIC — preserves external employee-events topic)
DROP STREAM IF EXISTS EMPLOYEE_EVENTS_RAW;

-- ============================================================
-- Stream from the raw employee-events topic (Dapr CloudEvent envelope)
-- data is VARCHAR because the Dapr outbox stringifies the JSON payload
-- ============================================================
CREATE STREAM EMPLOYEE_EVENTS_RAW (
  type VARCHAR,
  source VARCHAR,
  data VARCHAR
) WITH (
  KAFKA_TOPIC='employee-events',
  VALUE_FORMAT='JSON'
);

-- ============================================================
-- Filtered stream for clock-out and update events
-- Extracts fields from the stringified entity JSON via EXTRACTJSONFIELD
-- Entity fields (PascalCase): Id, EmployeeId, ClockIn, ClockOut, HoursWorked
-- Event type: DomainEvents[0].EventType
-- Pay period math: reference epoch 2024-01-01T00:00:00Z = 1704067200000 ms
-- Each bi-weekly period = 14 days = 1209600000 ms
-- Uses ClockIn for pay period (always present; ClockOut is null while clocked in)
-- SUBSTRING trims fractional seconds + Z so PARSE_TIMESTAMP can parse it
-- ============================================================
CREATE STREAM TIME_ENTRY_EVENTS AS
  SELECT
    EXTRACTJSONFIELD(data, '$.Id') AS TIME_ENTRY_ID,
    EXTRACTJSONFIELD(data, '$.EmployeeId') AS EMPLOYEE_ID,
    CAST(EXTRACTJSONFIELD(data, '$.HoursWorked') AS DOUBLE) AS HOURS_WORKED,
    EXTRACTJSONFIELD(data, '$.ClockIn') AS CLOCK_IN_TIME,
    EXTRACTJSONFIELD(data, '$.ClockOut') AS CLOCK_OUT_TIME,
    CAST(
      FLOOR(
        (UNIX_TIMESTAMP(PARSE_TIMESTAMP(
          SUBSTRING(EXTRACTJSONFIELD(data, '$.ClockIn'), 1, 19),
          'yyyy-MM-dd''T''HH:mm:ss'
        )) - 1704067200000) / 1209600000
      ) AS BIGINT
    ) AS PAY_PERIOD_NUMBER
  FROM EMPLOYEE_EVENTS_RAW
  WHERE EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') = 'timeentry.clockedout'
     OR EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') = 'timeentry.updated'
  EMIT CHANGES;

-- ============================================================
-- Materialized table: aggregate hours per employee per pay period
-- Writes every aggregation change to the payperiod-hours-changed Kafka topic
--
-- AS_MAP(COLLECT_LIST(id), COLLECT_LIST(hours)) builds a map keyed by
-- TIME_ENTRY_ID. Duplicate keys are overwritten with the latest value,
-- so edits replace the previous hours instead of adding to them.
-- REDUCE(MAP_VALUES(...)) then sums the deduplicated hours.
-- ============================================================
CREATE TABLE PAY_PERIOD_HOURS WITH (
  KAFKA_TOPIC='payperiod-hours-changed',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON',
  PARTITIONS=3
) AS
  SELECT
    EMPLOYEE_ID,
    PAY_PERIOD_NUMBER,
    REDUCE(
      MAP_VALUES(
        AS_MAP(
          COLLECT_LIST(TIME_ENTRY_ID),
          COLLECT_LIST(HOURS_WORKED)
        )
      ),
      CAST(0.0 AS DOUBLE),
      (s, x) => s + x
    ) AS TOTAL_HOURS_WORKED,
    COUNT(*) AS EVENT_COUNT,
    FORMAT_TIMESTAMP(
      FROM_UNIXTIME(1704067200000 + (PAY_PERIOD_NUMBER * 1209600000)),
      'yyyy-MM-dd''T''HH:mm:ss'
    ) AS PAY_PERIOD_START,
    FORMAT_TIMESTAMP(
      FROM_UNIXTIME(1704067200000 + ((PAY_PERIOD_NUMBER + 1) * 1209600000)),
      'yyyy-MM-dd''T''HH:mm:ss'
    ) AS PAY_PERIOD_END
  FROM TIME_ENTRY_EVENTS
  GROUP BY EMPLOYEE_ID, PAY_PERIOD_NUMBER
  EMIT CHANGES;
