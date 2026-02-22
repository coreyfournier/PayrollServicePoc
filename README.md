# Dapr POC - Employee Payroll System

A proof of concept demonstrating Dapr with Kafka pub/sub, MongoDB state store, the transactional outbox pattern, ksqlDB stream processing for real-time pay period aggregation, Kafka Streams for net pay calculation, and Elasticsearch-powered employee search. Two independent frontends consume the API: a REST+React app and a GraphQL+WebSocket subscription client.

## Architecture

This project follows Domain Driven Design (DDD) principles with the following layers:

- **Domain Layer**: Contains entities, value objects, domain events, and repository interfaces
- **Application Layer**: Contains DTOs, commands, queries, and handlers using MediatR
- **Infrastructure Layer**: Contains MongoDB repositories, Dapr event publishing, and data seeding
- **API Layer**: Contains ASP.NET Core controllers and Swagger documentation

### Write Path (Dapr Outbox Mode)

When `Features__UseDaprOutbox` is `true` (the default), writes follow a two-phase approach with the Dapr state store as the source of truth:

```
Controller → MediatR Handler → Entity (raises domain events)
  → DaprStateStoreUnitOfWork.ExecuteAsync()
      1. Dapr State Store Transaction  (entity + outbox — ATOMIC, SOURCE OF TRUTH)
      2. MongoDB Collection Write      (read model — BEST-EFFORT)
```

- **Step 1 fails** → exception propagates, nothing is written anywhere → fully consistent.
- **Step 2 fails** → entity is safely in the Dapr state store, `GetByIdAsync` still works (reads Dapr first), collection queries may be stale → acceptable trade-off for a POC.

Repository `AddAsync` methods use `ReplaceOneAsync` with `IsUpsert = true` so that retries after a Dapr success don't produce duplicate-key errors in MongoDB.

## Hours Worked → Net Pay

The end-to-end pipeline from clock-out to net pay spans Dapr, Kafka, ksqlDB, and a Kafka Streams application:

```
Clock-Out / Time Entry Update
  → employee-events (Kafka, via Dapr outbox)
    → ksqlDB: TIME_ENTRY_EVENTS stream
        (filters clock-out/update events, computes bi-weekly pay period number)
      → ksqlDB: EMPLOYEE_HOURS_BY_PERIOD table
          (deduplicates by time entry ID, sums hours per employee per pay period)
        → payperiod-hours-changed topic
    → ksqlDB: GROSS_PAY_EVENTS stream
        (captures employee + time entry events, normalizes employee ID)
      → ksqlDB: EMPLOYEE_GROSS_PAY_BY_PERIOD table
          (rate × hours for hourly; annual rate / 2080 × PayPeriodHours for salary)
        → employee-gross-pay topic
          → Net Pay Processor (Kafka Streams)
              Combines: gross pay + tax config + deductions
              Federal tax: progressive brackets (annualize → apply brackets → divide by 26)
              State tax: flat rate by state
              Deductions: fixed amount or percentage of gross
            → employee-net-pay topic
              → ListenerApi (Dapr subscription) → GraphQL subscription → UI
              → Elasticsearch Updater → employee-search topic → ES index → Search UI
```

### Key stages

1. **Hours aggregation** — ksqlDB deduplicates time entries by ID (edit-safe via `AS_MAP` + `REDUCE`) and sums hours per employee per bi-weekly pay period. Pay periods are 14 days starting from epoch 2024-01-01.
2. **Gross pay calculation** — ksqlDB multiplies pay rate × hours for hourly employees (`PayType=1`), or annual rate / 2080 × `PayPeriodHours` for salaried employees (`PayType=2`).
3. **Net pay calculation** — The Net Pay Processor applies federal progressive tax brackets (2024 rates, annualized), state flat rates, and deductions (fixed + percentage) to produce the final net pay breakdown per employee per pay period.

## Features

- **Employee Management**: CRUD operations for employee demographics (salary/hourly with pay rates)
- **Time Clock**: Clock in/out functionality with automatic hours calculation
- **Tax Information**: Federal and state tax withholding configuration
- **Deductions**: Various payroll deductions (health, dental, 401k, etc.)
- **Real-Time Pay Calculation**: End-to-end pipeline from clock-out through gross pay to net pay via ksqlDB and Kafka Streams
- **Event-Driven**: All data changes trigger domain events published to Kafka via Dapr
- **Transactional Consistency**: Dapr state store is the authoritative write path — entity state and outbox events are written atomically. MongoDB collections serve as a best-effort read model updated after the Dapr transaction succeeds.
- **Elasticsearch Search**: Full-text search with filter chips and an advanced query builder (AND/OR groups, nested field support) powered by Elasticsearch

## Prerequisites

- Docker and Docker Compose
- .NET 7.0 SDK (for local development)
- Dapr CLI (optional, for local debugging)

## Quick Start

1. **Start all services**:
   ```bash
   docker-compose up -d
   ```

2. **Seed data** (after services are healthy):
   ```bash
   docker-compose up seed
   ```
   Creates 5 employees, 40 time entries, 5 tax records, and 7 deductions via the REST API. The seed script clears existing data first, making it safe to re-run.

3. **Access the applications**:
   - Frontend (REST client): http://localhost:3000
   - Listener Client (GraphQL): http://localhost:3001
   - Swagger UI: http://localhost:5000/swagger
   - GraphQL Playground: http://localhost:5001/graphql

4. **View distributed traces**:
   - Zipkin: http://localhost:9411

## Listener API & Listener Client

The **Listener API** (`src/ListenerApi`) is a .NET 7.0 GraphQL server (HotChocolate) backed by MySQL. It subscribes to the `employee-events` and `employee-net-pay` Kafka topics via Dapr and persists employee records and pay attributes to its own database, demonstrating an event-driven read model. Events are processed idempotently using timestamp comparison. It exposes:

- **GraphQL queries** — fetch employee records from MySQL
- **GraphQL mutations** — manage records (e.g., delete all)
- **GraphQL subscriptions** — real-time WebSocket notifications when employee data changes

The **Listener Client** (`listenerClient/`) is a React + Vite frontend that connects to the Listener API using [urql](https://github.com/urql-graphql/urql) and `graphql-ws`. It provides two views:

- **Change Stream** — a live feed of employee changes pushed via GraphQL WebSocket subscriptions in real time
- **Employee Records** — a queryable list of all employee records stored in the Listener API's MySQL database

Together, they demonstrate an end-to-end event-driven pipeline: REST API mutation → domain event → Kafka → Dapr subscription → MySQL projection → GraphQL subscription → real-time UI update.

## ksqlDB Stream Processing

ksqlDB processes the `employee-events` Kafka topic through a pipeline of streams and tables defined in `ksqldb/statements.sql`. The `ksqldb-init` container executes these statements on startup.

### Streams

| Object | Source | Description |
|--------|--------|-------------|
| `EMPLOYEE_EVENTS_RAW` | `employee-events` topic | Base stream over the raw CloudEvent envelope. `data` is VARCHAR (not STRUCT) because Dapr's outbox stringifies the JSON payload. Fields are extracted via `EXTRACTJSONFIELD` with PascalCase names |
| `TIME_ENTRY_EVENTS` | `EMPLOYEE_EVENTS_RAW` | Filtered for `timeentry.clockedout` and `timeentry.updated` events. Extracts time entry ID, employee ID, hours worked, and computes a bi-weekly pay period number from the clock-in timestamp |
| `GROSS_PAY_EVENTS` | `EMPLOYEE_EVENTS_RAW` | Captures both employee events (pay rate/type changes) and time entry events. Normalizes employee ID via `COALESCE($.EmployeeId, $.Id)`. Uses `'__PAY_RATE__'` sentinel for employee events so they contribute 0 hours in the downstream dedup |
| `EMPLOYEE_INFO_EVENTS` | `EMPLOYEE_EVENTS_RAW` | Filtered for `employee.*` events. Feeds the `EMPLOYEE_INFO` table for search indexing |

### Tables

| Object | Type | Output Topic | Description |
|--------|------|------------|-------------|
| `EMPLOYEE_INFO` | Aggregation | `employee-info` | Latest employee state per ID (name, email, pay rate, etc.) for search pipeline consumption |
| `EMPLOYEE_HOURS_BY_PERIOD` | Aggregation | `payperiod-hours-changed` | Total hours per employee per pay period. Uses `AS_MAP(COLLECT_LIST(id), COLLECT_LIST(hours))` to deduplicate by time entry ID (last value wins), then `REDUCE(MAP_VALUES(...))` sums the latest hours. This prevents double-counting when time entries are edited |
| `EMPLOYEE_GROSS_PAY_BY_PERIOD` | Aggregation | `employee-gross-pay` | Gross pay per employee per pay period. Tracks pay rate via `LATEST_BY_OFFSET(PAY_RATE, true)` (ignores nulls from time entry events). For hourly employees (`PayType=1`): rate x summed hours. For salaried employees (`PayType=2`): annual rate / 2080 x `PayPeriodHours` |
| `EMPLOYEE_NET_PAY_BY_PERIOD` | Source | `employee-net-pay` | Read-only materialized view over the compacted `employee-net-pay` topic produced by NetPayProcessor. Queryable via pull queries |

## Net Pay Processor

A standalone Kafka Streams application (Java 17) in `src/NetPayProcessor/` that computes per-employee, per-pay-period net pay by combining gross pay with tax configuration and deductions. Connects directly to Kafka (no Dapr sidecar needed).

- **Inputs**: `employee-gross-pay` topic (from ksqlDB) + `employee-events` topic (taxinfo/deduction events)
- **State stores**: `gross-pay-store`, `tax-config-store`, `deduction-store`
- **Recomputes** on any input change — gross pay, tax config, or deduction update triggers a recalculation
- **Output**: `employee-net-pay` topic

Tax calculation applies federal progressive brackets (2024 rates, annualized by ×26 then /26) and simplified state flat rates (e.g., CA=9.3%, NY=6.85%, TX/WA=0%). Deductions are either fixed dollar amounts or a percentage of gross pay.

## Elasticsearch Search Pipeline

Three components work together to power the search experience:

1. **Elasticsearch Updater** (`src/ElasticsearchUpdater/`) — A Kafka consumer that combines data from the `employee-info` topic (latest employee state from ksqlDB) and the `employee-net-pay` topic (pay breakdowns from Net Pay Processor) into a single search document with the last 4 pay periods. Produces to the `employee-search` topic. Deactivated employees receive tombstone messages to remove them from the index.

2. **Kafka Connect ES Sink** — A connector registered by the seed script that upserts documents from the `employee-search` topic into the `employee-search` Elasticsearch index. Tombstones (null values) delete documents from ES.

3. **Frontend search** (`frontend/src/components/search/`) — React UI with two modes:
   - **Simple search** — text input with filter chips for pay type, active status, and pay period fields
   - **Advanced query builder** — AND/OR condition groups with nested field support for building precise queries

## Services

| Service | Port | Description |
|---------|------|-------------|
| payroll-api | 5000 | Payroll API Service (Swagger at /swagger) |
| listener-api | 5001 | GraphQL Listener API (/graphql) |
| frontend | 3000 | React frontend (REST client) |
| listener-client | 3001 | React frontend (GraphQL subscription client) |
| mongodb | 27017 | MongoDB Database |
| mysql | 3306 | MySQL Database (Listener API) |
| kafka | 9092/29092 | Kafka Message Broker |
| ksqldb-server | 8088 | ksqlDB REST API for stream processing |
| elasticsearch | 9200 | Search index |
| kafka-connect | 8083 | Kafka Connect (ES sink connector) |
| kafka-ui | 8080 | Kafka monitoring UI (also has ksqlDB query tab) |
| zookeeper | 2181 | Zookeeper (Kafka dependency) |
| zipkin | 9411 | Distributed Tracing |

## API Endpoints

### Employees
- `GET /api/employees` - Get all employees
- `GET /api/employees/{id}` - Get employee by ID
- `POST /api/employees` - Create employee
- `PUT /api/employees/{id}` - Update employee
- `DELETE /api/employees/{id}` - Deactivate employee

### Time Entries
- `GET /api/timeentries/employee/{employeeId}` - Get time entries for employee
- `POST /api/timeentries/clock-in/{employeeId}` - Clock in
- `POST /api/timeentries/clock-out/{employeeId}` - Clock out

### Tax Information
- `GET /api/taxinformation/employee/{employeeId}` - Get tax info
- `POST /api/taxinformation` - Create tax info
- `PUT /api/taxinformation/employee/{employeeId}` - Update tax info

### Deductions
- `GET /api/deductions/employee/{employeeId}` - Get deductions for employee
- `POST /api/deductions` - Create deduction
- `PUT /api/deductions/{id}` - Update deduction
- `DELETE /api/deductions/{id}` - Deactivate deduction

## Kafka Topics

The following topics are created by the `kafka-init` container on startup:

| Topic | Producer | Description |
|-------|----------|-------------|
| `employee-events` | Dapr outbox (payroll-api) | All entity events (employee, time entry, tax info, deduction) published via Dapr's transactional outbox as CloudEvent envelopes with stringified JSON `data` |
| `timeentry-events` | Dapr outbox (payroll-api) | Time entry create/update events (currently unused by downstream consumers) |
| `taxinfo-events` | Dapr outbox (payroll-api) | Tax information create/update events (currently unused by downstream consumers) |
| `deduction-events` | Dapr outbox (payroll-api) | Deduction create/update/deactivate events (currently unused by downstream consumers) |
| `payperiod-hours-changed` | ksqlDB | Aggregated hours per employee per pay period, produced by the `EMPLOYEE_HOURS_BY_PERIOD` table |
| `employee-gross-pay` | ksqlDB | Gross pay per employee per pay period (rate x hours), produced by the `EMPLOYEE_GROSS_PAY_BY_PERIOD` table |
| `employee-net-pay` | NetPayProcessor | Net pay breakdown per employee per pay period (gross - taxes - deductions). Compacted topic |
| `employee-info` | ksqlDB | Latest employee state per ID, produced by the `EMPLOYEE_INFO` table. Compacted topic |
| `employee-search` | ElasticsearchUpdater | Combined employee + last 4 pay period documents for ES indexing. Compacted topic |

Additional internal topics managed by ksqlDB (created/dropped by `ksqldb-init`):

| Topic | Description |
|-------|-------------|
| `TIME_ENTRY_EVENTS` | Filtered clock-out and time entry update events extracted from `employee-events` |
| `GROSS_PAY_EVENTS` | Combined employee and time entry events normalized for gross pay calculation |
| `EMPLOYEE_INFO_EVENTS` | Filtered employee events for the search pipeline |
| `employee-net-pay-by-period` | Materialized view of the `employee-net-pay` topic, queryable via ksqlDB pull queries |

## Seed Data

The database is seeded with 5 mock employees on startup:
1. John Smith (Salary - $75,000/year)
2. Sarah Johnson (Hourly - $28.50/hour)
3. Michael Williams (Salary - $85,000/year)
4. Emily Brown (Hourly - $32.00/hour)
5. David Davis (Salary - $95,000/year)

Each employee has associated tax information and some have deductions configured. The seed script also creates 40 time entries for the 2 hourly employees to exercise the full pay calculation pipeline.

## Local Development

1. **Start infrastructure only**:
   ```bash
   docker-compose up -d zookeeper kafka kafka-init mongodb zipkin
   ```

2. **Run the API with Dapr**:
   ```bash
   cd src/PayrollService.Api
   dapr run --app-id payroll-api --app-port 5000 --dapr-http-port 3500 --components-path ../../dapr/components --config ../../dapr/config.yaml -- dotnet run
   ```

3. **Connect to MongoDB with Compass**:
   ```
   mongodb://localhost:27017/?directConnection=true
   ```
   The `directConnection=true` parameter is required because the MongoDB container runs as a replica set with the Docker hostname `mongodb`. Without it, Compass attempts to resolve the replica set member hostname and fails with `getaddrinfo ENOTFOUND`.

## Project Structure

```
DaprPoc/
├── src/
│   ├── PayrollService.Api/           # ASP.NET Core API layer
│   │   ├── Controllers/
│   │   └── Program.cs
│   ├── PayrollService.Application/   # MediatR CQRS (commands, queries, DTOs)
│   ├── PayrollService.Domain/        # Entities, domain events, repository interfaces
│   ├── PayrollService.Infrastructure/ # MongoDB persistence, Dapr state store, event publishing
│   ├── ListenerApi/                  # HotChocolate GraphQL server (MySQL, Dapr subscriptions)
│   ├── ListenerApi.Data/             # EF Core entities and DbContext for ListenerApi
│   ├── NetPayProcessor/              # Kafka Streams net pay calculator (Java 17)
│   └── ElasticsearchUpdater/         # Kafka consumer for ES search indexing (Java 17)
├── frontend/                         # React + Vite REST client
│   └── src/components/search/        # Elasticsearch search UI (simple + advanced query builder)
├── listenerClient/                   # React + Vite GraphQL subscription client
├── dapr/
│   ├── components/                   # Dapr component configs (state store, pub/sub)
│   └── config.yaml
├── docker/
│   ├── Dockerfile                    # PayrollService.Api
│   ├── Dockerfile.listenerapi        # ListenerApi
│   └── Dockerfile.kafka-connect      # Kafka Connect with ES connector plugin
├── ksqldb/
│   └── statements.sql                # ksqlDB stream/table definitions
├── scripts/
│   └── seed.sh                       # API-based seed script
├── docker-compose.yaml
└── PayrollService.sln
```

## Cleanup

```bash
docker-compose down -v
```
