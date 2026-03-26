-- ksqlDB statements for employee info and net pay materialized views
-- Executed by ksqldb-init container on startup
--
-- Employee info is aggregated into the employee-info compacted topic for
-- the ElasticsearchUpdater. Net pay is materialized from the employee-net-pay
-- topic (produced by NetPayProcessor) for pull queries.
--
-- Gross pay aggregation (time entry hours, pay rate tracking) was moved from
-- ksqlDB to the NetPayProcessor Kafka Streams app for O(1) upserts instead
-- of unbounded COLLECT_LIST + AS_MAP + REDUCE.

-- ============================================================
-- Drop existing objects (reverse dependency order)
-- Covers both old names and new names for idempotent re-runs
-- ============================================================

-- Current / new objects
DROP TABLE IF EXISTS TRANSFER_USAGE_BY_DAY DELETE TOPIC;
DROP TABLE IF EXISTS TRANSFER_USAGE_BY_PERIOD DELETE TOPIC;
DROP STREAM IF EXISTS TRANSFER_COMPLETED DELETE TOPIC;
DROP STREAM IF EXISTS TRANSFER_EVENTS_RAW;
DROP TABLE IF EXISTS TRANSFER_LIMITS;
DROP TABLE IF EXISTS EMPLOYEE_INFO DELETE TOPIC;
DROP STREAM IF EXISTS EMPLOYEE_INFO_EVENTS DELETE TOPIC;
DROP TABLE IF EXISTS EMPLOYEE_NET_PAY_BY_PERIOD;

-- Legacy: previous versions used ksqlDB for gross pay aggregation
DROP STREAM IF EXISTS EMPLOYEE_NET_PAY;
DROP TABLE IF EXISTS EMPLOYEE_GROSS_PAY_BY_PERIOD DELETE TOPIC;
DROP TABLE IF EXISTS EMPLOYEE_GROSS_PAY DELETE TOPIC;
DROP STREAM IF EXISTS GROSS_PAY_EVENTS DELETE TOPIC;
DROP TABLE IF EXISTS EMPLOYEE_HOURS_BY_PERIOD DELETE TOPIC;
DROP TABLE IF EXISTS PAY_PERIOD_HOURS_BY_PERIOD DELETE TOPIC;
DROP TABLE IF EXISTS PAY_PERIOD_HOURS DELETE TOPIC;
DROP STREAM IF EXISTS TIME_ENTRY_EVENTS DELETE TOPIC;
DROP TABLE IF EXISTS TIME_ENTRY_LATEST_HOURS DELETE TOPIC;
DROP STREAM IF EXISTS CLOCKOUT_EVENTS DELETE TOPIC;

-- Base stream last (no DELETE TOPIC — preserves external employee-events topic)
DROP STREAM IF EXISTS EMPLOYEE_EVENTS_RAW;

-- ============================================================
-- Stream from the raw employee-events topic (CloudEvent envelope)
-- data is a STRUCT covering all entity field shapes (Employee, TimeEntry).
-- Missing fields for a given event type will be null.
-- ============================================================
CREATE STREAM EMPLOYEE_EVENTS_RAW (
  type VARCHAR,
  source VARCHAR,
  data STRUCT<
    Id VARCHAR,
    EmployeeId VARCHAR,
    FirstName VARCHAR,
    LastName VARCHAR,
    Email VARCHAR,
    PayType INTEGER,
    PayRate DOUBLE,
    PayPeriodHours DOUBLE,
    IsActive BOOLEAN,
    HireDate VARCHAR,
    ClockIn VARCHAR,
    ClockOut VARCHAR,
    HoursWorked DOUBLE,
    UpdatedAt VARCHAR,
    CreatedAt VARCHAR,
    DomainEvents ARRAY<STRUCT<EventId VARCHAR, OccurredOn VARCHAR, EventType VARCHAR>>
  >
) WITH (
  KAFKA_TOPIC='employee-events',
  VALUE_FORMAT='JSON'
);

-- ============================================================
-- Stream: extract employee info from employee.* events
-- Captures employee.created and employee.updated events with
-- all employee fields needed for the Elasticsearch search index
-- ============================================================
CREATE STREAM EMPLOYEE_INFO_EVENTS AS
  SELECT
    data->Id AS EMPLOYEE_ID,
    data->FirstName AS FIRST_NAME,
    data->LastName AS LAST_NAME,
    data->Email AS EMAIL,
    CAST(data->PayType AS VARCHAR) AS PAY_TYPE,
    data->PayRate AS PAY_RATE,
    data->PayPeriodHours AS PAY_PERIOD_HOURS,
    CAST(data->IsActive AS VARCHAR) AS IS_ACTIVE,
    data->HireDate AS HIRE_DATE,
    data->DomainEvents[1]->EventType AS EVENT_TYPE
  FROM EMPLOYEE_EVENTS_RAW
  WHERE data->DomainEvents[1]->EventType LIKE 'employee.%'
  EMIT CHANGES;

-- ============================================================
-- Table: latest employee state per ID → employee-info compacted topic
-- Used by ElasticsearchUpdater to build combined search documents
-- ============================================================
CREATE TABLE EMPLOYEE_INFO WITH (
  KAFKA_TOPIC='employee-info',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON',
  PARTITIONS=3
) AS
  SELECT
    EMPLOYEE_ID,
    LATEST_BY_OFFSET(FIRST_NAME) AS FIRST_NAME,
    LATEST_BY_OFFSET(LAST_NAME) AS LAST_NAME,
    LATEST_BY_OFFSET(EMAIL) AS EMAIL,
    LATEST_BY_OFFSET(PAY_TYPE) AS PAY_TYPE,
    LATEST_BY_OFFSET(PAY_RATE) AS PAY_RATE,
    LATEST_BY_OFFSET(PAY_PERIOD_HOURS) AS PAY_PERIOD_HOURS,
    LATEST_BY_OFFSET(IS_ACTIVE) AS IS_ACTIVE,
    LATEST_BY_OFFSET(HIRE_DATE) AS HIRE_DATE,
    LATEST_BY_OFFSET(EVENT_TYPE) AS LAST_EVENT_TYPE
  FROM EMPLOYEE_INFO_EVENTS
  GROUP BY EMPLOYEE_ID
  EMIT CHANGES;

-- ============================================================
-- Source table: employee net pay per pay period
-- Backed by the compacted employee-net-pay topic (produced by NetPayProcessor).
-- SOURCE TABLE reads the compacted topic directly — tombstones (null values)
-- emitted by NetPayProcessor for deactivated employees delete rows automatically.
-- Key columns match the JSON key: {"EMPLOYEE_ID":"...","PAY_PERIOD_NUMBER":55}
-- Value fields are UPPER_SNAKE_CASE matching Java NetPayResult serialization.
-- Queryable as a pull query:
--   SELECT * FROM EMPLOYEE_NET_PAY_BY_PERIOD;
--   SELECT * FROM EMPLOYEE_NET_PAY_BY_PERIOD
--     WHERE EMPLOYEE_ID = '...' AND PAY_PERIOD_NUMBER = 55;
-- ============================================================
CREATE SOURCE TABLE EMPLOYEE_NET_PAY_BY_PERIOD (
  EMPLOYEE_ID VARCHAR PRIMARY KEY,
  PAY_PERIOD_NUMBER BIGINT PRIMARY KEY,
  GROSS_PAY DOUBLE,
  FEDERAL_TAX DOUBLE,
  STATE_TAX DOUBLE,
  ADDITIONAL_FEDERAL_WITHHOLDING DOUBLE,
  ADDITIONAL_STATE_WITHHOLDING DOUBLE,
  TOTAL_TAX DOUBLE,
  TOTAL_FIXED_DEDUCTIONS DOUBLE,
  TOTAL_PERCENT_DEDUCTIONS DOUBLE,
  TOTAL_DEDUCTIONS DOUBLE,
  NET_PAY DOUBLE,
  PAY_RATE DOUBLE,
  PAY_TYPE VARCHAR,
  TOTAL_HOURS_WORKED DOUBLE,
  PAY_PERIOD_START VARCHAR,
  PAY_PERIOD_END VARCHAR
) WITH (
  KAFKA_TOPIC='employee-net-pay',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON'
);

-- ============================================================
-- Transfer usage aggregation
-- Streams and tables for tracking completed transfer counts
-- and amounts per employee per pay period and per day.
-- ============================================================

-- Stream from the raw transfer-events topic (CloudEvent envelope)
-- The data field contains the full Transfer entity serialized by TransferEventPublisher.
CREATE STREAM TRANSFER_EVENTS_RAW (
  id VARCHAR,
  source VARCHAR,
  type VARCHAR,
  data STRUCT<
    Id VARCHAR,
    EmployeeId VARCHAR,
    Amount DOUBLE,
    PayPeriodNumber BIGINT,
    Status VARCHAR,
    InitiatedAt VARCHAR,
    CompletedAt VARCHAR,
    BankAccountId VARCHAR,
    FailureReason VARCHAR,
    ExternalReferenceId VARCHAR,
    CurrentBalance DOUBLE,
    DomainEvents ARRAY<STRUCT<EventId VARCHAR, OccurredOn VARCHAR, EventType VARCHAR>>
  >
) WITH (
  KAFKA_TOPIC='transfer-events',
  VALUE_FORMAT='JSON'
);

-- Stream: filter for completed transfers only
CREATE STREAM TRANSFER_COMPLETED AS
  SELECT
    data->EmployeeId AS EMPLOYEE_ID,
    data->Amount AS AMOUNT,
    data->PayPeriodNumber AS PAY_PERIOD_NUMBER,
    data->InitiatedAt AS INITIATED_AT,
    SUBSTRING(data->InitiatedAt, 1, 10) AS INITIATED_DATE,
    data->Id AS TRANSFER_ID
  FROM TRANSFER_EVENTS_RAW
  WHERE data->Status = 'Completed'
  EMIT CHANGES;

-- Table: transfer usage per employee per pay period
-- Aggregates count and total amount of completed transfers
CREATE TABLE TRANSFER_USAGE_BY_PERIOD WITH (
  KAFKA_TOPIC='transfer-usage-by-period',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON'
) AS
  SELECT
    EMPLOYEE_ID,
    PAY_PERIOD_NUMBER,
    COUNT(*) AS TRANSFER_COUNT,
    SUM(AMOUNT) AS TOTAL_AMOUNT
  FROM TRANSFER_COMPLETED
  GROUP BY EMPLOYEE_ID, PAY_PERIOD_NUMBER
  EMIT CHANGES;

-- Table: transfer usage per employee per day (based on InitiatedAt date)
-- Aggregates daily count of completed transfers
CREATE TABLE TRANSFER_USAGE_BY_DAY WITH (
  KAFKA_TOPIC='transfer-usage-by-day',
  KEY_FORMAT='JSON',
  VALUE_FORMAT='JSON'
) AS
  SELECT
    EMPLOYEE_ID,
    INITIATED_DATE,
    COUNT(*) AS TRANSFER_COUNT
  FROM TRANSFER_COMPLETED
  GROUP BY EMPLOYEE_ID, INITIATED_DATE
  EMIT CHANGES;

-- ============================================================
-- Source table: employee transfer limits (latest per employee)
-- Backed by the compacted transfer-limits topic published by transfer-api.
-- Queryable via pull query:
--   SELECT * FROM TRANSFER_LIMITS WHERE EMPLOYEE_ID = '...';
-- ============================================================
CREATE SOURCE TABLE TRANSFER_LIMITS (
  EMPLOYEE_ID VARCHAR PRIMARY KEY,
  MAX_PER_PAY_PERIOD INTEGER,
  MAX_AMOUNT_PER_PAY_PERIOD DOUBLE,
  MAX_PER_DAY INTEGER
) WITH (
  KAFKA_TOPIC='transfer-limits',
  VALUE_FORMAT='JSON'
);
