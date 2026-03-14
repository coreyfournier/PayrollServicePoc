# Employee Payroll System POC

A proof of concept demonstrating MassTransit with Kafka Rider, MongoDB transactional outbox, ksqlDB stream processing for real-time pay period aggregation, Kafka Streams for net pay calculation, and Elasticsearch-powered employee search. Two independent frontends consume the API: a REST+React app and a GraphQL+WebSocket subscription client.

## Architecture

This project follows Domain Driven Design (DDD) principles with the following layers:

- **Domain Layer**: Contains entities, value objects, domain events, and repository interfaces
- **Application Layer**: Contains DTOs, commands, queries, and handlers using MediatR
- **Infrastructure Layer**: Contains MongoDB repositories, MassTransit event publishing, and data seeding
- **API Layer**: Contains ASP.NET Core controllers and Swagger documentation

### Write Path (MassTransit Transactional Outbox)

Writes use the MassTransit MongoDB transactional outbox to atomically persist entity state and publish domain events:

```
Controller → MediatR Handler → Entity (raises domain events)
  → MongoDbUnitOfWork.ExecuteAsync()
      MongoDB Transaction (entity + outbox messages — ATOMIC)
        → MassTransit Outbox Delivery Service → Kafka (via Kafka Rider)
```

- Entity state and outbox messages are written in a single MongoDB transaction — if it fails, nothing is written.
- The MassTransit outbox delivery service picks up pending messages and publishes them to Kafka via the Kafka Rider.
- This eliminates the dual-write problem: events are guaranteed to publish if the entity persists.

## Hours Worked → Net Pay

The end-to-end pipeline from clock-out to net pay spans MassTransit, Kafka, ksqlDB, and a Kafka Streams application:

```
Clock-Out / Time Entry Update
  → employee-events (Kafka, via MassTransit outbox)
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
              → ListenerApi (MassTransit Kafka consumer) → GraphQL subscription → UI
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
- **Event-Driven**: All data changes trigger domain events published to Kafka via MassTransit Kafka Rider
- **Transactional Consistency**: MassTransit MongoDB transactional outbox writes entity state and outbox messages atomically in a single MongoDB transaction. The outbox delivery service publishes events to Kafka reliably.
- **Elasticsearch Search**: Full-text search with filter chips and an advanced query builder (AND/OR groups, nested field support) powered by Elasticsearch

## Prerequisites

- Docker and Docker Compose
- .NET 7.0 SDK (for local development)

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
   - PayrollPro Client (GraphQL): http://localhost:3001
   - Swagger UI: http://localhost:5000/swagger
   - GraphQL Playground: http://localhost:5001/graphql

4. **View distributed traces**:
   - Zipkin: http://localhost:9411

## Listener API & PayrollPro Client

The **Listener API** (`src/ListenerApi`) is a .NET 7.0 GraphQL server (HotChocolate) backed by MySQL. It subscribes to the `employee-events` and `employee-net-pay` Kafka topics via MassTransit Kafka Rider and persists employee records and pay attributes to its own database, demonstrating an event-driven read model. Events are processed idempotently using timestamp comparison. It exposes:

- **GraphQL queries** — fetch employee records from MySQL
- **GraphQL mutations** — manage records (e.g., delete all)
- **GraphQL subscriptions** — real-time WebSocket notifications when employee data changes

The **PayrollPro Client** (`payrollProClient/`) is a React + Vite frontend that connects to the Listener API using [urql](https://github.com/urql-graphql/urql) and `graphql-ws`. It provides two views:

- **Change Stream** — a live feed of employee changes pushed via GraphQL WebSocket subscriptions in real time
- **Employee Records** — a queryable list of all employee records stored in the Listener API's MySQL database

Together, they demonstrate an end-to-end event-driven pipeline: REST API mutation → domain event → Kafka → MassTransit consumer → MySQL projection → GraphQL subscription → real-time UI update.

## ksqlDB Stream Processing

ksqlDB processes the `employee-events` Kafka topic through a pipeline of streams and tables defined in `ksqldb/statements.sql`. The `ksqldb-init` container executes these statements on startup.

### Streams

| Object | Source | Description |
|--------|--------|-------------|
| `EMPLOYEE_EVENTS_RAW` | `employee-events` topic | Base stream over the raw event envelope. `data` is VARCHAR (not STRUCT) because the outbox stringifies the JSON payload. Fields are extracted via `EXTRACTJSONFIELD` with PascalCase names |
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

A standalone Kafka Streams application (Java 17) in `src/NetPayProcessor/` that computes per-employee, per-pay-period net pay by combining gross pay with tax configuration and deductions. Connects directly to Kafka.

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

## Transfer Service (Separate Bounded Context)

The transfer feature is a fully independent bounded context (`TransferService.*`) demonstrating MassTransit Saga state machines, the Debezium Outbox Pattern, and CDC-based command dispatch — all integrated through Kafka.

### Architecture Overview

```
PayrollPro Client / Frontend
    │
    ├─► Direct: POST transfer-api/api/Transfers
    │     └─► MassTransit consumer → TransferSaga state machine
    │
    └─► Async: POST listener-api/api/Transfer
          └─► MySQL Transaction (TransferRecord + OutboxMessage)
                └─► Debezium CDC → Kafka → TransferSaga state machine
```

TransferService has its own MongoDB database (`transfer_db`) — completely independent of PayrollService.

### Debezium Outbox Pattern (Command Dispatch)

ListenerApi uses the **Debezium Outbox Pattern** — an industry-standard approach used by Netflix, Airbnb, WePay, and Zalando. Instead of publishing directly to Kafka (which creates a dual-write problem), a single MySQL transaction atomically writes both the client read model and a command envelope:

```
BEGIN MySQL Transaction
  INSERT TransferRecord     (status = "Queued", visible to client immediately)
  INSERT OutboxMessage      (topic = "transfer-requests", payload = command JSON)
COMMIT
```

**Debezium's MySQL CDC connector** (running as a Kafka Connect plugin) tails the MySQL binlog and routes each outbox row to the Kafka topic specified in the row's `Topic` column, using `AggregateId` as the Kafka message key to preserve per-employee ordering.

This guarantees:
- **Atomicity** — the transfer record and the command to process it succeed or fail together in one MySQL transaction
- **Guaranteed delivery** — Debezium handles publish, retry, and offset tracking via the binlog
- **Repeatable reads** — the client always sees the transfer on refresh (MySQL is the source of truth for the read model)
- **No custom publisher** — no background polling service; Debezium runs as a Kafka Connect connector already in the stack
- **Low latency** — binlog tailing is near-real-time (~100ms)

The same outbox pattern is used for both transfer initiation and accept/reject commands. A single Kafka topic (`transfer-requests`) carries both, routed by an `Action` field in the JSON payload.

### Change Data Capture (CDC) Pipeline

```
MySQL (listener_db)
  │
  │ binlog stream
  ▼
Debezium MySQL Source Connector (Kafka Connect)
  │ outbox-event-router SMT
  │   • Reads Topic column → routes to correct Kafka topic
  │   • Reads AggregateId → sets as Kafka message key (ordering)
  │   • Reads Payload → sends as message value
  ▼
Kafka (transfer-requests topic)
  │
  ▼
TransferService.Api (MassTransit Kafka consumer on transfer-requests)
  │ Routes by Action field:
  │   • null/missing → initiate transfer (via TransferSaga)
  │   • "accept-balance" → raise saga event (BalanceAccepted)
  ▼
TransferSaga State Machine
```

### MassTransit Saga State Machine (Concurrency & Orchestration)

`TransferSaga` is a MassTransit saga state machine that manages the entire transfer lifecycle. The saga instance is correlated by `employeeId`, and MassTransit's MongoDB saga repository provides concurrency control — concurrent transfers for the same employee are serialized automatically.

The saga handles:

1. Validates bank account ownership
2. Checks transfer limits (daily count, pay period count, pay period amount)
3. Creates the Transfer entity atomically via the MassTransit MongoDB transactional outbox

The saga then orchestrates the transfer through its states:

```
TransferSaga State Machine
  Initiated
    → VerifyBalance               → query ksqlDB for employee's current net pay
    │   ├─ Balance sufficient     → transition to Processing
    │   └─ Balance insufficient   → transition to AwaitingConfirmation
    │       └─ BalanceAccepted event (24h timeout via scheduled message)
    │           ├─ Accepted       → transition to Processing
    │           └─ Rejected/timeout → transition to Failed
  Processing
    → ExecuteBankTransfer         → call SimulatedBankService
    │   └─ Retry up to 3× with exponential backoff (2s, 4s, 8s)
    → Completed or Failed
```

Each state change publishes events to the `transfer-events` Kafka topic via the MassTransit outbox. ListenerApi subscribes to `transfer-events` and updates the MySQL `TransferRecords` table, which feeds GraphQL subscriptions for real-time UI updates.

### Transfer Limits

Configurable via environment variables (`TransferLimits__MaxPerPayPeriod`, `TransferLimits__MaxAmountPerPayPeriod`, `TransferLimits__MaxPerDay`). Enforced authoritatively by TransferService inside the saga. ListenerApi performs a best-effort pre-check from its materialized MySQL data. The `GET /api/transfers/employee/{id}/limits` endpoint returns current usage and a `canTransfer` boolean.

### Kafka Topics (Transfers)

| Topic | Producer | Consumer | Description |
|-------|----------|----------|-------------|
| `transfer-requests` | Debezium CDC (from ListenerApi MySQL outbox) | TransferService.Api (MassTransit Kafka consumer) | Commands: initiate transfer, accept/reject balance change |
| `transfer-events` | TransferService.Api (MassTransit outbox) | ListenerApi (MassTransit Kafka consumer) | State changes: Initiated, AwaitingConfirmation, Processing, Completed, Failed |

### Two Databases

| Database | Technology | Role | Written By |
|----------|-----------|------|-----------|
| `transfer_db` | MongoDB | Authoritative transfer & bank account data | TransferService via MassTransit MongoDB transactional outbox |
| `listener_db.TransferRecords` | MySQL | Client read model, GraphQL queries & subscriptions | ListenerApi (from `transfer-events` Kafka topic) |

### Outbox Cleanup

OutboxMessages rows accumulate in MySQL unless cleaned up. A **MySQL scheduled event** (created via EF Core migration, applied automatically on startup) purges rows older than 2 hours:

```sql
CREATE EVENT cleanup_outbox_messages
  ON SCHEDULE EVERY 2 HOUR
  DO DELETE FROM OutboxMessages WHERE CreatedAt < NOW() - INTERVAL 2 HOUR;
```

This is safe because Debezium reads from the MySQL **binlog**, not the table. Once a row is INSERTed, that INSERT is permanently in the binlog. Debezium tracks its binlog offset, so it sees every INSERT regardless of whether the row still exists. The 2-hour retention is a convenience window for debugging.

### Simulated Bank Service

`SimulatedBankService` adds random delays (1–10s) and ~20% failure rate to test the workflow's retry logic with exponential backoff.

### End-to-End Data Flow

```
Client (PayrollPro Client or Frontend)
  │
  │ POST listener-api/api/Transfer
  ▼
ListenerApi ─── MySQL Transaction ──┐
  │                                 │
  │  TransferRecord (Queued)        │  OutboxMessage
  │  ← client sees immediately      │  (topic: transfer-requests)
  │                                 │
  │                                 ▼
  │                           MySQL Binlog
  │                                 │
  │                           Debezium CDC
  │                           (Kafka Connect)
  │                                 │
  │                                 ▼
  │                     Kafka: transfer-requests
  │                                 │
  │                                 ▼
  │                     TransferService.Api
  │                       TransferSaga State Machine
  │                         │ validate → limits check → create
  │                         │ verify balance → process → bank transfer
  │                         │
  │                         │ each state change →
  │                         │   MassTransit MongoDB outbox
  │                         │     → Kafka: transfer-events
  │                         ▼
  │                     Kafka: transfer-events
  │                                 │
  │◄────────────────────────────────┘
  │  MassTransit Kafka consumer
  │  UPDATE TransferRecord in MySQL
  │    Queued → Initiated → Processing → Completed
  │
  ▼
GraphQL subscription → PayrollPro Client (real-time UI update)
```

## Services

| Service | Port | Description |
|---------|------|-------------|
| payroll-api | 5000 | Payroll API Service (Swagger at /swagger) |
| transfer-api | 5002 | Transfer API Service (Swagger at /swagger) |
| listener-api | 5001 | GraphQL Listener API (/graphql) |
| frontend | 3000 | React frontend (REST client) |
| payrollpro-client | 3001 | React frontend (GraphQL subscription client) |
| mongodb | 27017 | MongoDB Database |
| mysql | 3306 | MySQL Database (Listener API) |
| kafka | 9092/29092 | Kafka Message Broker |
| ksqldb-server | 8088 | ksqlDB REST API for stream processing |
| elasticsearch | 9200 | Search index |
| kafka-connect | 8083 | Kafka Connect (ES sink + Debezium MySQL CDC) |
| kafka-ui | 8089 | Kafka monitoring UI (also has ksqlDB query tab) |
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

### Transfers (transfer-api, port 5002)
- `POST /api/transfers` - Initiate a transfer (via TransferSaga)
- `GET /api/transfers/recent?limit=50&status=Processing` - Get recent transfers with optional status filter
- `GET /api/transfers/employee/{employeeId}` - Get transfers for employee
- `GET /api/transfers/{id}` - Get transfer by ID
- `GET /api/transfers/{id}/saga` - Get saga state for a transfer
- `POST /api/transfers/{id}/accept` - Accept or reject a balance change (direct)
- `GET /api/transfers/employee/{employeeId}/limits` - Get transfer limits and current usage

### Transfers (listener-api, port 5001 — async via outbox)
- `POST /api/transfer` - Initiate a transfer (via Debezium outbox → Kafka → TransferSaga)
- `POST /api/transfer/{id}/accept` - Accept/reject balance change (via Debezium outbox → Kafka → saga event)
- `GET /api/transfer/employee/{employeeId}` - Get transfers for employee (from MySQL read model)
- `GET /api/transfer/employee/{employeeId}/limits` - Get transfer limits (best-effort pre-check)

### Bank Accounts (transfer-api, port 5002)
- `GET /api/bankaccounts/employee/{employeeId}` - Get bank accounts for employee
- `POST /api/bankaccounts` - Create bank account
- `DELETE /api/bankaccounts/{id}` - Delete bank account

## Kafka Topics

The following topics are created by the `kafka-init` container on startup:

| Topic | Producer | Description |
|-------|----------|-------------|
| `employee-events` | MassTransit outbox (payroll-api) | All entity events (employee, time entry, tax info, deduction) published via MassTransit MongoDB transactional outbox |
| `timeentry-events` | MassTransit outbox (payroll-api) | Time entry create/update events (currently unused by downstream consumers) |
| `taxinfo-events` | MassTransit outbox (payroll-api) | Tax information create/update events (currently unused by downstream consumers) |
| `deduction-events` | MassTransit outbox (payroll-api) | Deduction create/update/deactivate events (currently unused by downstream consumers) |
| `payperiod-hours-changed` | ksqlDB | Aggregated hours per employee per pay period, produced by the `EMPLOYEE_HOURS_BY_PERIOD` table |
| `employee-gross-pay` | ksqlDB | Gross pay per employee per pay period (rate x hours), produced by the `EMPLOYEE_GROSS_PAY_BY_PERIOD` table |
| `employee-net-pay` | NetPayProcessor | Net pay breakdown per employee per pay period (gross - taxes - deductions). Compacted topic |
| `employee-info` | ksqlDB | Latest employee state per ID, produced by the `EMPLOYEE_INFO` table. Compacted topic |
| `employee-search` | ElasticsearchUpdater | Combined employee + last 4 pay period documents for ES indexing. Compacted topic |
| `transfer-requests` | Debezium CDC (ListenerApi MySQL outbox) | Transfer commands dispatched via CDC: initiate transfer and accept/reject balance change |
| `transfer-events` | MassTransit outbox (transfer-api) | Transfer state changes published via MassTransit outbox (Initiated, AwaitingConfirmation, Processing, Completed, Failed) |

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

2. **Run the API**:
   ```bash
   cd src/PayrollService.Api
   dotnet run
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
│   ├── PayrollService.Infrastructure/ # MongoDB persistence, MassTransit outbox, event publishing
│   ├── TransferService.Api/          # Transfer API: MassTransit Saga, controllers
│   │   └── Sagas/TransferSaga.cs     # MassTransit Saga state machine (concurrency + orchestration)
│   ├── TransferService.Application/  # Transfer MediatR CQRS (commands, queries, DTOs)
│   ├── TransferService.Domain/       # Transfer entities, value objects, repository interfaces
│   ├── TransferService.Infrastructure/ # MongoDB persistence, MassTransit outbox, bank service
│   ├── ListenerApi/                  # HotChocolate GraphQL server (MySQL, MassTransit consumers)
│   │   └── Controllers/TransferController.cs  # Debezium outbox command dispatch
│   ├── ListenerApi.Data/             # EF Core entities and DbContext for ListenerApi
│   │   └── Entities/OutboxMessage.cs # Debezium outbox entity
│   ├── NetPayProcessor/              # Kafka Streams net pay calculator (Java 17)
│   └── ElasticsearchUpdater/         # Kafka consumer for ES search indexing (Java 17)
├── frontend/                         # React + Vite REST client
│   └── src/components/search/        # Elasticsearch search UI (simple + advanced query builder)
├── payrollProClient/                 # React + Vite GraphQL subscription client
├── docker/
│   ├── Dockerfile                    # PayrollService.Api
│   ├── Dockerfile.listenerapi        # ListenerApi
│   ├── Dockerfile.transferapi        # TransferService.Api
│   └── Dockerfile.kafka-connect      # Kafka Connect with ES sink + Debezium MySQL CDC
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

# Key takeaways
## Air Gapped Domains
* All services are fully independent of each other with mixed storage technology. 
* Domain knowledge is not shared.
* Domains are not required to have high uptime.

## Subscriber Database Recovery
1. Navigate to the Employee Change Listener
1. Choose Delete All Records
1. Recover Employees
   1. Stop `listener-api` container so it unsubscribes from Kafka
   1. In the Kafka UI http://localhost:8089/ui/clusters/payroll-cluster/consumer-groups/listener-api-group choose the ellipsis and Reset Offset to Earliest for all partitions and employee-events.
   1. Start the container `listener-api` and watch new records get added.
1. Recover net pay
   1. Stop `listener-api` container so it unsubscribes from Kafka
   1. In the Kafka UI http://localhost:8089/ui/clusters/payroll-cluster/consumer-groups/listener-api-group choose the ellipsis and Reset Offset to Earliest for all partitions and employee-net-pay.
   1. Start the container `listener-api` and watch the net pay get updated on the employee.

## Event-Driven Read Model Projections
* The same `employee-events` stream powers three independent read models (MongoDB for REST queries, MySQL for GraphQL, Elasticsearch for search).
* New read models can be added without touching the write side.

## Transactional Outbox Pattern
* Entity state and domain events are written atomically, eliminating the dual-write problem.
* Events are guaranteed to publish if the entity persists — no "wrote to DB but failed to publish" inconsistency.

## Debezium CDC Outbox (Transfer Commands)
* Transfer commands are dispatched via the Debezium Outbox Pattern — a single MySQL transaction writes the read model and a command envelope, and Debezium tails the binlog to publish to Kafka.
* No custom publisher or background polling service — Debezium handles publish, retry, and offset tracking as a Kafka Connect connector.
* The same pattern handles both transfer initiation and accept/reject commands via a single Kafka topic with action-based routing.

## Saga-Based Concurrency & Orchestration
* MassTransit Saga state machines manage the transfer lifecycle with automatic state persistence in MongoDB.
* Saga instances are correlated by employee ID, serializing concurrent transfers and eliminating race conditions without manual locking.
* External events (balance accept/reject) can pause and resume the saga with configurable timeouts via scheduled messages.
* Failed bank transfers retry with exponential backoff — the saga state survives service restarts.

## Derived Data via Stream Processing
* Business calculations (hours aggregation, gross pay, net pay) happen in specialized processors outside the write service.
* The API doesn't know how pay is calculated — it just publishes events.

## Edit-Safe Aggregation
* The ksqlDB `AS_MAP` + `REDUCE` pattern handles corrections without double-counting.
* Editing a time entry replaces its hours instead of adding a duplicate — critical for payroll accuracy.

## Polyglot Architecture
* .NET API, Java Kafka Streams, ksqlDB SQL, React frontends — all integrated through Kafka as the universal backbone.
* Each component uses the best tool for its job.

## MassTransit Transport Abstraction
* Pub/sub transport is configured via MassTransit's Kafka Rider.
* MassTransit supports swapping Kafka for RabbitMQ, Azure Service Bus, or Amazon SQS with minimal code changes.

## Kafka as Durable Event Log
* Events are retained indefinitely, enabling replay (as the subscriber recovery demonstrates), new consumer bootstrapping, and audit trails.
* The elasticsearch-updater pre-scans topics from the beginning on every startup to rebuild its in-memory state.

## Self-Healing Components
* net-pay-processor and elasticsearch-updater detect topic loss, wait for recreation, and restart their full lifecycle automatically.
* No manual intervention needed.

## Graceful Degradation
* If Elasticsearch goes down, payroll and the employee directory still work.
* If the ListenerApi goes down, the REST API is unaffected.
* Failures are isolated, not cascading. No single point of failure takes down the whole system.

## Audit Trail / Compliance
* Every state change is an immutable event in Kafka.
* You can reconstruct the full history of any employee's pay changes — important for payroll audits and regulatory compliance.

## Add Capabilities Without Disruption
* Need a new report, a notification service, or an export to a third-party system? Subscribe to the existing event stream.
* Zero changes to existing services, zero risk to current functionality.

## Zero-Downtime Deployments
* Updating tax calculation logic doesn't require redeploying the API or frontends.
* Kafka buffers events while a service is restarting.

## Team Autonomy
* Each service has clear ownership boundaries.
* The search team, payroll team, and reporting team can ship independently with different release cadences.

## Temporal Decoupling
* Producers and consumers don't need to be online simultaneously.
* Events buffer in Kafka. A service deployed next month can bootstrap from today's event history.

## Business Logic Isolation
* Changing tax brackets in the Net Pay Processor can't break time entry aggregation in ksqlDB.
* Each calculation stage is independently deployable and testable.

## Testability
* Each component can be tested in isolation with synthetic messages.
* The Net Pay Processor doesn't need the full stack — just feed it Kafka records.