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
