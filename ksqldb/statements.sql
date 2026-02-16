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
DROP TABLE IF EXISTS EMPLOYEE_NET_PAY_BY_PERIOD DELETE TOPIC;
DROP STREAM IF EXISTS EMPLOYEE_NET_PAY;
DROP TABLE IF EXISTS EMPLOYEE_GROSS_PAY_BY_PERIOD DELETE TOPIC;
DROP STREAM IF EXISTS GROSS_PAY_EVENTS DELETE TOPIC;
DROP TABLE IF EXISTS PAY_PERIOD_HOURS_BY_PERIOD DELETE TOPIC;
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
CREATE TABLE PAY_PERIOD_HOURS_BY_PERIOD WITH (
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

-- ============================================================
-- Filtered stream for gross pay calculation
-- Captures BOTH employee events (pay rate changes) and time entry events
-- (hours worked) from the single employee-events topic.
-- Employee ID is normalized: TimeEntry has $.EmployeeId, Employee uses $.Id.
-- TIME_ENTRY_ID uses sentinel '__PAY_RATE__' for employee events so the
-- AS_MAP dedup in the downstream table treats rate changes as a 0-hour entry.
-- Pay period is derived from $.ClockIn (timeentry) or $.UpdatedAt (employee).
-- ============================================================
CREATE STREAM GROSS_PAY_EVENTS AS
  SELECT
    COALESCE(
      EXTRACTJSONFIELD(data, '$.EmployeeId'),
      EXTRACTJSONFIELD(data, '$.Id')
    ) AS EMPLOYEE_ID,
    CASE
      WHEN EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') LIKE 'timeentry.%'
        THEN EXTRACTJSONFIELD(data, '$.Id')
      ELSE '__PAY_RATE__'
    END AS TIME_ENTRY_ID,
    COALESCE(CAST(EXTRACTJSONFIELD(data, '$.HoursWorked') AS DOUBLE), CAST(0.0 AS DOUBLE)) AS HOURS_WORKED,
    CAST(EXTRACTJSONFIELD(data, '$.PayRate') AS DOUBLE) AS PAY_RATE,
    EXTRACTJSONFIELD(data, '$.PayType') AS PAY_TYPE,
    CAST(EXTRACTJSONFIELD(data, '$.PayPeriodHours') AS DOUBLE) AS PAY_PERIOD_HOURS,
    CAST(
      FLOOR(
        (UNIX_TIMESTAMP(PARSE_TIMESTAMP(
          SUBSTRING(
            COALESCE(
              EXTRACTJSONFIELD(data, '$.ClockIn'),
              EXTRACTJSONFIELD(data, '$.UpdatedAt')
            ), 1, 19),
          'yyyy-MM-dd''T''HH:mm:ss'
        )) - 1704067200000) / 1209600000
      ) AS BIGINT
    ) AS PAY_PERIOD_NUMBER
  FROM EMPLOYEE_EVENTS_RAW
  WHERE EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') = 'employee.created'
     OR EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') = 'employee.updated'
     OR EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') = 'timeentry.clockedout'
     OR EXTRACTJSONFIELD(data, '$.DomainEvents[0].EventType') = 'timeentry.updated'
  EMIT CHANGES;

-- ============================================================
-- Materialized table: employee gross pay per pay period
-- Writes every aggregation change to the employee-gross-pay Kafka topic
--
-- PAY_RATE / PAY_TYPE: LATEST_BY_OFFSET(col, true) ignores nulls, so
-- timeentry events (which have null pay rate) don't overwrite the rate.
-- TOTAL_HOURS_WORKED: same AS_MAP + COLLECT_LIST + REDUCE dedup pattern
-- as PAY_PERIOD_HOURS_BY_PERIOD — the '__PAY_RATE__' sentinel contributes 0 hours.
-- EFFECTIVE_HOURLY_RATE: for Salary (PayType=2), divides annual rate by
-- 2080 hours (52 weeks × 40 hrs). For Hourly (PayType=1), rate is $/hour.
-- GROSS_PAY: EFFECTIVE_HOURLY_RATE × TOTAL_HOURS_WORKED
-- ============================================================
CREATE TABLE EMPLOYEE_GROSS_PAY_BY_PERIOD WITH (
  KAFKA_TOPIC='employee-gross-pay',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON',
  PARTITIONS=3
) AS
  SELECT
    EMPLOYEE_ID,
    PAY_PERIOD_NUMBER,
    LATEST_BY_OFFSET(PAY_RATE, true) AS PAY_RATE,
    LATEST_BY_OFFSET(PAY_TYPE, true) AS PAY_TYPE,
    LATEST_BY_OFFSET(PAY_PERIOD_HOURS, true) AS PAY_PERIOD_HOURS,
    CASE
      WHEN LATEST_BY_OFFSET(PAY_TYPE, true) = '2'
        THEN LATEST_BY_OFFSET(PAY_PERIOD_HOURS, true)
      ELSE REDUCE(
        MAP_VALUES(
          AS_MAP(
            COLLECT_LIST(TIME_ENTRY_ID),
            COLLECT_LIST(HOURS_WORKED)
          )
        ),
        CAST(0.0 AS DOUBLE),
        (s, x) => s + x
      )
    END AS TOTAL_HOURS_WORKED,
    CASE
      WHEN LATEST_BY_OFFSET(PAY_TYPE, true) = '2'
        THEN LATEST_BY_OFFSET(PAY_RATE, true) / 2080.0
      ELSE LATEST_BY_OFFSET(PAY_RATE, true)
    END AS EFFECTIVE_HOURLY_RATE,
    CASE
      WHEN LATEST_BY_OFFSET(PAY_TYPE, true) = '2'
        THEN LATEST_BY_OFFSET(PAY_RATE, true) / 2080.0
      ELSE LATEST_BY_OFFSET(PAY_RATE, true)
    END
    *
    CASE
      WHEN LATEST_BY_OFFSET(PAY_TYPE, true) = '2'
        THEN LATEST_BY_OFFSET(PAY_PERIOD_HOURS, true)
      ELSE REDUCE(
        MAP_VALUES(
          AS_MAP(
            COLLECT_LIST(TIME_ENTRY_ID),
            COLLECT_LIST(HOURS_WORKED)
          )
        ),
        CAST(0.0 AS DOUBLE),
        (s, x) => s + x
      )
    END AS GROSS_PAY,
    FORMAT_TIMESTAMP(
      FROM_UNIXTIME(1704067200000 + (PAY_PERIOD_NUMBER * 1209600000)),
      'yyyy-MM-dd''T''HH:mm:ss'
    ) AS PAY_PERIOD_START,
    FORMAT_TIMESTAMP(
      FROM_UNIXTIME(1704067200000 + ((PAY_PERIOD_NUMBER + 1) * 1209600000)),
      'yyyy-MM-dd''T''HH:mm:ss'
    ) AS PAY_PERIOD_END,
    COUNT(*) AS EVENT_COUNT
  FROM GROSS_PAY_EVENTS
  GROUP BY EMPLOYEE_ID, PAY_PERIOD_NUMBER
  EMIT CHANGES;

-- ============================================================
-- Stream over the employee-net-pay topic (produced by NetPayProcessor)
-- No DELETE TOPIC — preserves the external topic managed by NetPayProcessor.
-- Key columns match the JSON key: {"employeeId":"...","payPeriodNumber":55}
-- Value fields are camelCase matching Java NetPayResult serialization.
-- ============================================================
CREATE STREAM EMPLOYEE_NET_PAY (
  employeeId VARCHAR KEY,
  payPeriodNumber BIGINT KEY,
  grossPay DOUBLE,
  federalTax DOUBLE,
  stateTax DOUBLE,
  additionalFederalWithholding DOUBLE,
  additionalStateWithholding DOUBLE,
  totalTax DOUBLE,
  totalFixedDeductions DOUBLE,
  totalPercentDeductions DOUBLE,
  totalDeductions DOUBLE,
  netPay DOUBLE,
  payRate DOUBLE,
  payType VARCHAR,
  totalHoursWorked DOUBLE,
  payPeriodStart VARCHAR,
  payPeriodEnd VARCHAR
) WITH (
  KAFKA_TOPIC='employee-net-pay',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON'
);

-- ============================================================
-- Materialized table: employee net pay per pay period
-- Tracks the latest net pay breakdown per employee + pay period.
-- Each NetPayProcessor emission replaces the previous state via
-- LATEST_BY_OFFSET, so the table always reflects current net pay.
-- Queryable as a pull query (point lookup):
--   SELECT * FROM EMPLOYEE_NET_PAY_BY_PERIOD
--     WHERE EMPLOYEEID = '...' AND PAYPERIODNUMBER = 55;
-- Or as a push query:
--   SELECT * FROM EMPLOYEE_NET_PAY_BY_PERIOD EMIT CHANGES;
-- ============================================================
CREATE TABLE EMPLOYEE_NET_PAY_BY_PERIOD WITH (
  KAFKA_TOPIC='employee-net-pay-by-period',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON',
  PARTITIONS=3
) AS
  SELECT
    employeeId,
    payPeriodNumber,
    LATEST_BY_OFFSET(grossPay) AS GROSS_PAY,
    LATEST_BY_OFFSET(federalTax) AS FEDERAL_TAX,
    LATEST_BY_OFFSET(stateTax) AS STATE_TAX,
    LATEST_BY_OFFSET(additionalFederalWithholding) AS ADDITIONAL_FEDERAL_WITHHOLDING,
    LATEST_BY_OFFSET(additionalStateWithholding) AS ADDITIONAL_STATE_WITHHOLDING,
    LATEST_BY_OFFSET(totalTax) AS TOTAL_TAX,
    LATEST_BY_OFFSET(totalFixedDeductions) AS TOTAL_FIXED_DEDUCTIONS,
    LATEST_BY_OFFSET(totalPercentDeductions) AS TOTAL_PERCENT_DEDUCTIONS,
    LATEST_BY_OFFSET(totalDeductions) AS TOTAL_DEDUCTIONS,
    LATEST_BY_OFFSET(netPay) AS NET_PAY,
    LATEST_BY_OFFSET(payRate) AS PAY_RATE,
    LATEST_BY_OFFSET(payType) AS PAY_TYPE,
    LATEST_BY_OFFSET(totalHoursWorked) AS TOTAL_HOURS_WORKED,
    LATEST_BY_OFFSET(payPeriodStart) AS PAY_PERIOD_START,
    LATEST_BY_OFFSET(payPeriodEnd) AS PAY_PERIOD_END,
    COUNT(*) AS EVENT_COUNT
  FROM EMPLOYEE_NET_PAY
  GROUP BY employeeId, payPeriodNumber
  EMIT CHANGES;
