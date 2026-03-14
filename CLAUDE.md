# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Employee payroll system POC demonstrating MassTransit with Kafka pub/sub, MongoDB, the transactional outbox pattern, and ksqlDB stream processing for real-time pay period aggregation. Two independent frontends consume the API: a REST+React app and a GraphQL+WebSocket subscription client.

## Common Commands

### Full stack (Docker)
```bash
docker-compose up -d              # Start everything
docker-compose down -v            # Tear down with volumes
docker-compose up -d zookeeper kafka mongodb zipkin  # Infrastructure only
```

### Backend build (local)
```bash
dotnet build PayrollService.sln
dotnet run --project src/PayrollService.Api
```

### Frontend (REST client)
```bash
cd frontend && npm install && npm run dev    # Dev server
cd frontend && npm run build                 # Production build
cd frontend && npm run lint                  # ESLint
```

### PayrollPro Client (GraphQL client)
```bash
cd payrollProClient && npm install && npm run dev
```

### Seed data
```bash
docker-compose up seed              # Run after the stack is up
```

The seed service is the **single initialization entry point** — it creates Kafka topics, initializes ksqlDB streams/tables, registers the Elasticsearch sink connector, and seeds data (5 employees, 40 time entries, 5 tax records, 7 deductions) via the REST API. This exercises the full event pipeline (MassTransit outbox → Kafka → ksqlDB → ListenerApi → GraphQL). The script (`scripts/seed.sh`) clears existing data first, making it safe to re-run. Requires `payroll-api`, `listener-api`, `kafka`, `elasticsearch`, and `kafka-connect` to be healthy.

The `kafka-init` and `ksqldb-init` services are available standalone under the `init` profile (`docker-compose --profile init up kafka-init`) but do not auto-start — seed handles everything.

### Running tests
```bash
dotnet test tests/PayrollService.UnitTests/        # 32 payroll domain/application tests
dotnet test tests/TransferService.UnitTests/        # 18 transfer domain tests
cd frontend && npm test                             # 63 frontend component/hook tests
cd frontend && npm run lint                         # ESLint — CI runs this too, must pass
```

**Important:** Always run `npm run lint` in addition to `npm test` for the frontend. CI runs both and will fail on lint errors even if all tests pass.

## Architecture

### DDD Layers (PayrollService.*)

```
Api (.NET 9.0)  →  Application (.NET 7.0)  →  Domain (.NET 7.0)
                                                      ↑
                                            Infrastructure (.NET 7.0)
```

- **Domain**: Entities (`Employee`, `TimeEntry`, `TaxInformation`, `Deduction`), domain events, repository interfaces. Base `Entity` class collects domain events in-memory. `Employee.PayPeriodHours` (decimal, default 40) specifies hours per pay period for salaried employees; used by ksqlDB to calculate gross pay instead of time entries.
- **Application**: MediatR CQRS — commands for writes, queries for reads, DTOs for API boundaries.
- **Infrastructure**: MongoDB persistence, MassTransit Kafka integration, event publishing via Confluent.Kafka producer with CloudEvent wrapper. Contains `DependencyInjection.cs` for all service registration.
- **Api**: ASP.NET Core controllers, Swagger UI at `/swagger`.

### DDD Layers (TransferService.*)

```
Api (.NET 9.0)  →  Application (.NET 7.0)  →  Domain (.NET 7.0)
                                                      ↑
                                            Infrastructure (.NET 7.0)
```

Separate bounded context for bank transfers, extracted from PayrollService. Has its own MongoDB database (`transfer_db`).

- **Domain**: Entities (`Transfer`, `BankAccount`), value objects (`TransferLimits`), domain events, repository interfaces.
- **Application**: MediatR CQRS — transfer initiation, bank account CRUD, transfer limits queries.
- **Infrastructure**: Separate `TransferMongoDbContext`, MassTransit outbox publishing to `transfer-events` topic, ksqlDB balance service, simulated bank service.
- **Api**: ASP.NET Core controllers, MassTransit saga state machine for transfer orchestration. Runs on port 5002.

The frontend nginx config routes `/api/transfers/` and `/api/bankaccounts/` to `transfer-api`, all other `/api/` requests to `payroll-api`.

### Write Path (MassTransitUnitOfWork)

```
Controller → MediatR Handler → Entity (raises domain events)
  → MassTransitUnitOfWork.ExecuteAsync()
      1. MongoDB Write (entity persisted)
      2. Kafka Publish via Confluent.Kafka producer (domain events wrapped in CloudEvent envelope)
  → ListenerApi (MassTransit Kafka Rider consumer) → MySQL → GraphQL subscription → PayrollPro Client
```

Repository `AddAsync` methods use `ReplaceOneAsync` with `IsUpsert = true` to be idempotent — retries don't produce duplicate-key errors in MongoDB.

### ListenerApi (.NET 7.0)

Separate service: HotChocolate GraphQL server backed by MySQL (Pomelo EF Core). Subscribes to Kafka `employee-events` and `employee-net-pay` topics via MassTransit Kafka Rider consumers. Processes events idempotently (checks `LastEventTimestamp` for employees, `PayPeriodNumber` for pay attributes). Broadcasts changes to WebSocket subscribers via in-memory `ITopicEventSender`. Auto-applies EF Core migrations on startup.

Consumers parse CloudEvent JSON envelopes for backward compatibility with the ksqlDB pipeline (which expects the CloudEvent `data` field as a stringified JSON payload).

**Entities:**
- `EmployeeRecord` — employee data from `employee-events` topic
- `EmployeePayAttributes` — 1:1 with `EmployeeRecord`, stores latest pay period net pay breakdown from `employee-net-pay` topic. PK = `EmployeeId` (FK to `EmployeeRecord`, cascade delete). Exposed via GraphQL as `payAttributes` nested field on employees.

### Service Ports (Docker)

| Service | Port | Notes |
|---------|------|-------|
| payroll-api | 5000 | Swagger at /swagger |
| transfer-api | 5002 | Swagger at /swagger, transfers & bank accounts |
| listener-api | 5001 | GraphQL at /graphql |
| frontend | 3000 | REST client |
| payrollpro-client | 3001 | GraphQL client |
| kafka | 9092 (internal), 29092 (host) | |
| kafka-ui | 8089 | Also has ksqlDB query UI |
| ksqldb-server | 8088 | REST API |
| mongodb | 27017 | Replica set, connect with `?directConnection=true` |
| mysql | 3306 | |
| elasticsearch | 9200 | Search index |
| kafka-connect | 8083 | ES sink connector REST API |
| zipkin | 9411 | Distributed tracing |

### Kafka Topics

`employee-events`, `timeentry-events`, `taxinfo-events`, `deduction-events`, `employee-net-pay`, `employee-search`, `employee-info` — created by the seed script (topic creation runs before ksqlDB initialization). Additional internal topics (`employee-net-pay-by-period`, `EMPLOYEE_INFO_EVENTS`) are managed by ksqlDB. **Important:** `employee-info` must be pre-created with 3 partitions before ksqlDB's `EMPLOYEE_INFO` CTAS runs; the seed script ensures this ordering.

### ksqlDB Stream Processing

ksqlDB processes the `employee-events` Kafka topic to produce the `employee-info` compacted topic (latest employee state per ID) for the ElasticsearchUpdater, and materializes the `employee-net-pay` topic as a SOURCE TABLE for pull queries. Defined in `ksqldb/statements.sql`, executed by the seed script on startup (after topic creation).

**Pipeline (4 objects):**

```
employee-events topic
  → EMPLOYEE_EVENTS_RAW stream (raw CloudEvent envelope, data as STRUCT)
  └→ EMPLOYEE_INFO_EVENTS stream (filtered for employee.* events)
      → EMPLOYEE_INFO table (latest employee state per ID → employee-info topic)

employee-net-pay topic (produced by NetPayProcessor)
  → EMPLOYEE_NET_PAY_BY_PERIOD source table (keyed by EMPLOYEE_ID + PAY_PERIOD_NUMBER)
```

**Key design decisions:**

- **Event type filtering** — The top-level CloudEvent `type` is always `com.dapr.event.sent`. The actual event type is extracted from `data->DomainEvents[1]->EventType` (ksqlDB arrays are 1-indexed).
- **Pay period math** — Bi-weekly periods starting from epoch 2024-01-01T00:00:00Z (1704067200000 ms), each 14 days (1209600000 ms).

**Init behavior:** The seed script (and standalone `ksqldb-init` if run via `--profile init`) terminates all running queries before executing DROP/CREATE statements, making it safe for re-runs.

**Querying ksqlDB:**
- Kafka UI at http://localhost:8089 (KSQL DB tab in sidebar)
- CLI: `docker exec -it ksqldb-server ksql http://localhost:8088`
- REST: `curl http://localhost:8088/ksql -H 'Content-Type: application/vnd.ksql.v1+json' -d '{"ksql": "SHOW TABLES;"}'`

### Net Pay Processor (Kafka Streams, Java 17)

Standalone Kafka Streams application (`src/NetPayProcessor/`) that computes per-employee, per-pay-period gross pay and net pay from a single `employee-events` topic. Handles employee info (pay rate), time entries (hours), tax configuration, and deductions. Connects directly to Kafka.

**Pipeline:**

```
employee-events topic ──→ Kafka Streams App ──→ employee-net-pay topic
  (employee.*, timeentry.*,   (net-pay-processor)
   taxinfo.*, deduction.*)
```

**Internal topology** (Processor API with in-memory state):
- `employeeInfoStore` — keyed by `employeeId` → {payRate, payType, payPeriodHours}
- `hoursStore` — keyed by `employeeId:payPeriod` → Map<timeEntryId, hoursWorked> (O(1) upserts)
- `taxConfigStore` — keyed by `employeeId` → tax config (filing status, state, additional withholding)
- `deductionStore` — keyed by `employeeId` → map of `deductionId → {amount, isPercentage, isActive}`

**Gross pay computation** (previously in ksqlDB, moved for O(1) performance):
- **Hourly** (`PayType=1`): `payRate × sum(hoursStore values)` — each time entry upsert is O(1) into the map, sum is O(unique entries)
- **Salary** (`PayType=2`): `(payRate / 2080) × payPeriodHours` — no time entry tracking needed
- **Edit-safe**: time entry edits overwrite the existing map entry by ID, not append — no unbounded list growth
- **Current period only** — Pay rate changes are assigned to the pay period derived from `UpdatedAt`

When an **employee event** arrives: update employeeInfoStore → compute gross pay → compute net pay → emit.
When a **time entry event** arrives: upsert into hoursStore → compute gross pay → compute net pay → emit.
When a **tax info event** arrives: update taxConfigStore → recompute net pay for current period → emit.
When a **deduction event** arrives: update deductionStore → recompute net pay for current period → emit.

**Tax calculation:**
- **Federal**: Progressive brackets (2024 rates) — annualizes bi-weekly gross x 26, applies brackets, divides by 26. Single/HeadOfHousehold use single brackets; Married uses married brackets.
- **State**: Simplified flat rates per state (e.g., CA=9.3%, NY=6.85%, TX=0%, WA=0%, IL=4.95%).
- **Additional withholding**: `AdditionalFederalWithholding` and `AdditionalStateWithholding` from tax info events added on top.

**Deduction calculation:**
- **Fixed** (`isPercentage=false`): subtracted directly
- **Percentage** (`isPercentage=true`): `(amount/100) x grossPay`
- **Inactive** (`isActive=false`): contribute $0 (kept in map for reactivation)

**`employee-net-pay` topic schema:**
- Key (JSON): `{"EMPLOYEE_ID": "...", "PAY_PERIOD_NUMBER": 55}`
- Value (JSON): `{"EMPLOYEE_ID": "...", "PAY_PERIOD_NUMBER": 55, "GROSS_PAY": 4440.0, "FEDERAL_TAX": 170.77, "STATE_TAX": 0.0, "ADDITIONAL_FEDERAL_WITHHOLDING": 50.0, "ADDITIONAL_STATE_WITHHOLDING": 25.0, "TOTAL_TAX": 245.77, "TOTAL_FIXED_DEDUCTIONS": 225.0, "TOTAL_PERCENT_DEDUCTIONS": 0.0, "TOTAL_DEDUCTIONS": 225.0, "NET_PAY": 3969.23, "PAY_RATE": 28.5, "PAY_TYPE": "1", "TOTAL_HOURS_WORKED": 155.75, "PAY_PERIOD_START": "2026-02-09T00:00:00", "PAY_PERIOD_END": "2026-02-23T00:00:00"}`

**`employee-net-pay-by-period` topic schema (ksqlDB SOURCE TABLE):**
- Key (JSON): `{"EMPLOYEE_ID": "...", "PAY_PERIOD_NUMBER": 55}`
- Value (JSON): `{"GROSS_PAY": 4440.0, "FEDERAL_TAX": 170.77, "STATE_TAX": 0.0, "ADDITIONAL_FEDERAL_WITHHOLDING": 50.0, "ADDITIONAL_STATE_WITHHOLDING": 25.0, "TOTAL_TAX": 245.77, "TOTAL_FIXED_DEDUCTIONS": 225.0, "TOTAL_PERCENT_DEDUCTIONS": 0.0, "TOTAL_DEDUCTIONS": 225.0, "NET_PAY": 3969.23, "PAY_RATE": 28.5, "PAY_TYPE": "1", "TOTAL_HOURS_WORKED": 155.75, "PAY_PERIOD_START": "2026-02-09T00:00:00", "PAY_PERIOD_END": "2026-02-23T00:00:00"}`
- Queryable via pull query: `SELECT * FROM EMPLOYEE_NET_PAY_BY_PERIOD WHERE EMPLOYEE_ID = '...' AND PAY_PERIOD_NUMBER = 55;`

**Key files:**
- `src/NetPayProcessor/pom.xml` — Maven project (kafka-streams, jackson)
- `src/NetPayProcessor/src/main/java/com/payroll/netpay/NetPayApp.java` — topology + main
- `src/NetPayProcessor/src/main/java/com/payroll/netpay/NetPayProcessor.java` — unified processor
- `src/NetPayProcessor/src/main/java/com/payroll/netpay/TaxCalculator.java` — progressive bracket + state tax logic

### Elasticsearch Updater (Kafka Consumer, Java 17)

Standalone Kafka consumer application (`src/ElasticsearchUpdater/`) that combines employee info with their last 4 pay periods into a single search document. Connects directly to Kafka.

**Pipeline:**

```
employee-info topic ──────────┐
                               ├──→ ElasticsearchUpdater ──→ employee-search topic
employee-net-pay topic ────────┘                                    │
                                                                    ▼
                                                         Kafka Connect ES Sink
                                                                    │
                                                                    ▼
                                                         Elasticsearch (employee-search index)
```

**In-memory state:**
- `employeeInfoMap` — keyed by employee ID, latest employee data from `employee-info` topic
- `payPeriodsMap` — keyed by employee ID, `TreeMap<payPeriodNumber, PayPeriodRecord>` (auto-sorted, trimmed to last 4)

**Behavior:**
- On startup, pre-scans both input topics from the beginning to rebuild in-memory state
- On `employee-info` event: updates employee info, produces combined document
- On `employee-net-pay` event: adds/updates pay period in TreeMap, trims to last 4, produces combined document
- If employee is deactivated (`IS_ACTIVE = "false"`), produces a tombstone (null value) to `employee-search`

**`employee-search` topic schema:**
- Key (string): employee ID GUID (plain string, not JSON)
- Value (JSON): `{"employee_id": "...", "first_name": "John", "last_name": "Smith", "email": "...", "pay_type": "2", "pay_rate": 75000.0, "pay_period_hours": 40.0, "is_active": true, "hire_date": "...", "pay_periods": [{"pay_period_number": 55, "gross_pay": 1442.31, ...}, ...]}`
- `pay_periods` array contains at most 4 entries (most recent pay periods)
- Topic uses compacted cleanup policy (latest document per employee retained)

**Kafka Connect ES Sink Connector:**
- Registered by the seed script at `kafka-connect:8083`
- Upserts documents from `employee-search` topic into the `employee-search` Elasticsearch index
- Employee ID string becomes ES document `_id`
- Tombstones (null values) delete documents from ES

**Key files:**
- `src/ElasticsearchUpdater/pom.xml` — Maven project (kafka-clients, jackson)
- `src/ElasticsearchUpdater/src/main/java/com/payroll/esupdater/ElasticsearchUpdaterApp.java` — consumer loop + pre-scan
- `src/ElasticsearchUpdater/src/main/java/com/payroll/esupdater/EmployeeSearchDocument.java` — combined document POJO
- `src/ElasticsearchUpdater/src/main/java/com/payroll/esupdater/PayPeriodRecord.java` — pay period POJO
- `docker/Dockerfile.kafka-connect` — Kafka Connect image with ES connector plugin

### MongoDB

Runs as a single-node replica set (`rs0`) to support multi-document transactions. Replica set is auto-initialized via the container healthcheck script.

### Transfer Workflow

The transfer feature is a **separate bounded context** (`TransferService.*`) with its own database (`transfer_db`). It demonstrates several advanced architecture patterns.

**Debezium Outbox Pattern (Transfer Command Dispatch):**

ListenerApi uses the **Debezium Outbox Pattern** — an industry-standard approach used by Netflix, Airbnb, WePay, and Zalando. A single MySQL transaction atomically writes the `TransferRecord` (client read model) and an `OutboxMessage` (command envelope). Debezium's MySQL CDC connector tails the binlog and routes each outbox row to the Kafka topic specified in the row's `Topic` column, using `AggregateId` as the Kafka message key (preserving per-employee ordering). This guarantees:
- **Atomicity** — both succeed or both fail in one MySQL transaction
- **Guaranteed delivery** — Debezium handles publish, retry, and offset tracking
- **Repeatable reads** — the client always sees the transfer on refresh (MySQL is source of truth)
- **No custom publisher** — Debezium runs as a Kafka Connect connector (already in the stack)
- **Low latency** — binlog tailing is near-real-time (~100ms)

**Outbox cleanup:** A MySQL scheduled event (`cleanup_outbox_messages`) purges rows older than 2 hours. This is safe because Debezium reads from the MySQL **binlog**, not the table — once a row is INSERTed, the binlog records it permanently regardless of whether the row is later deleted. The 2-hour window is for debugging visibility only. Requires `--event-scheduler=ON` in MySQL (configured in docker-compose). Created via EF Core migration (`20260311120000_AddOutboxCleanupEvent`), applied automatically on ListenerApi startup.

Key files: `src/ListenerApi.Data/Entities/OutboxMessage.cs`, `src/ListenerApi/Controllers/TransferController.cs`, `docker/Dockerfile.kafka-connect` (Debezium plugin), `scripts/seed.sh` (connector registration).

See `docs/transfer-outbox-options.md` for the full comparison of approaches evaluated and outbox cleanup details.

**Messages** (defined in `src/TransferService.Application/Messages/TransferMessages.cs`):
- `TransferRequested` — published when a transfer is initiated (via outbox or direct API)
- `BalanceAccepted` — published when a user accepts/rejects an insufficient balance
- `ConfirmationTimedOut` — scheduled by the saga, fires after 24h if balance not confirmed
- `RetryBankTransfer` — scheduled by the saga for exponential backoff retries (2s, 4s, 8s)

**End-to-End Data Flow:**
```
Client (REST/GraphQL)
  │
  ├─► Direct: POST transfer-api:5002/api/Transfers
  │     └─► Publishes TransferRequested → TransferStateMachine (saga keyed by transferId)
  │           ├─ Validate bank account ownership
  │           ├─ Check transfer limits (daily/period count/amount)
  │           ├─ Create Transfer entity → MassTransitUnitOfWork
  │           │    ├─ MongoDB transfer_db write
  │           │    └─ Kafka publish (transfer-events)
  │           └─ Saga transitions through states
  │
  └─► Async: ListenerApi (Debezium Outbox) → Kafka (transfer-requests)
        └─► TransferRequestConsumer publishes TransferRequested → same saga path

TransferStateMachine (src/TransferService.Api/Sagas/TransferStateMachine.cs):
  States: Submitted → BalanceVerified | AwaitingConfirmation → Processing → Completed | Failed

  1. TransferRequested → Submitted — validate, query ksqlDB for employee net pay
  │   ├─ Balance sufficient → BalanceVerified
  │   └─ Balance insufficient → AwaitingConfirmation
  │       └─ Schedule ConfirmationTimedOut (24h) via MassTransit Schedule<>
  │           ├─ BalanceAccepted (accepted=true) → BalanceVerified
  │           └─ BalanceAccepted (accepted=false) / ConfirmationTimedOut → Failed
  2. BalanceVerified → Processing — call SimulatedBankService
  │   └─ On failure: schedule RetryBankTransfer (up to 3x, exponential backoff)
  3. Processing → Completed or Failed
  │
  Each state change → MassTransitUnitOfWork → Kafka (transfer-events)
                                                    │
                                                    ▼
                                          ListenerApi (MassTransit Kafka Rider consumer)
                                                    │
                                                    ▼
                                          MySQL listener_db.TransferRecords
                                                    │
                                                    ▼
                                          GraphQL subscription → PayrollPro Client
```

**Two Databases:**
- **transfer_db** (MongoDB) — authoritative transfer and bank account data. Collections: `transfers`, `bank_accounts`.
- **listener_db.TransferRecords** (MySQL) — read model materialized from Kafka `transfer-events` topic via ListenerApi. Used for client queries and GraphQL subscriptions.

**MassTransit Saga State Machine for Concurrency Control:**
- `TransferStateMachine` orchestrates the full transfer lifecycle. Concurrency is handled by MongoDB saga document-level locking — concurrent messages for the same saga instance are serialized at the database level.
- The saga checks transfer limits (per pay period count, per period amount, per day count), creates the Transfer entity, and transitions through states.
- The 24h confirmation timeout uses MassTransit's `Schedule<>` instead of external timer infrastructure.
- Bank transfer retries use scheduled messages with exponential backoff.

**Transfer Limits:**
- Configurable via environment variables: `TransferLimits__MaxPerPayPeriod`, `TransferLimits__MaxAmountPerPayPeriod`, `TransferLimits__MaxPerDay`.
- Transfer-api enforces authoritatively inside the saga. ListenerApi does a best-effort pre-check from its own materialized MySQL data.
- The `GET /api/transfers/employee/{id}/limits` endpoint returns limits + current usage + `canTransfer` boolean.

**Simulated Bank Service:**
- `SimulatedBankService` adds random delays (1-10s) and ~20% failure rate to test the retry logic.

**MassTransit Kafka Rider Configuration:**
- Kafka producer/consumer configuration is done in `Program.cs` for each service via MassTransit's Kafka Rider.
- Transfer-api uses a separate consumer group (`transfer-service-group`) from payroll-api.

**Docker Services:**
- `transfer-api` (port 5002) — TransferService.Api container, depends on MongoDB and Kafka.

**Key Files:**
- `src/TransferService.Application/Messages/TransferMessages.cs` — all transfer events: `TransferRequested`, `BalanceAccepted`, `ConfirmationTimedOut`, `RetryBankTransfer`
- `src/TransferService.Api/Sagas/TransferStateMachine.cs` — MassTransit saga state machine for transfer orchestration
- `src/TransferService.Api/Sagas/TransferState.cs` — saga state entity
- `src/TransferService.Api/Consumers/TransferRequestConsumer.cs` — consumes transfer-requests from Kafka, publishes `TransferRequested` / `BalanceAccepted`
- `src/TransferService.Infrastructure/ExternalServices/SimulatedBankService.cs` — simulated bank
- `src/TransferService.Infrastructure/ExternalServices/KsqlDbBalanceService.cs` — ksqlDB balance queries
- `src/ListenerApi/Controllers/TransferController.cs` — async transfer initiation via Debezium outbox + queries

### Kafka Topics (Transfers)

- `transfer-requests` — async commands from ListenerApi to transfer-api
- `transfer-events` — transfer state changes from transfer-api to ListenerApi (via MassTransit outbox)

### Key Files

- `src/PayrollService.Api/Program.cs` — DI setup, MassTransit Kafka Rider configuration
- `src/PayrollService.Infrastructure/DependencyInjection.cs` — all infrastructure service registration
- `src/PayrollService.Infrastructure/Messaging/MassTransitUnitOfWork.cs` — MongoDB write + Kafka publish
- `src/PayrollService.Infrastructure/Messaging/CloudEventWrapper.cs` — wraps domain events in CloudEvent envelope
- `src/PayrollService.Domain/Common/Entity.cs` — base entity with domain event collection
- `src/ListenerApi/Program.cs` — GraphQL schema, MassTransit Kafka Rider consumers, migration runner
- `src/ListenerApi/Consumers/` — Kafka Rider consumer classes (employee-events, employee-net-pay, transfer-events)
- `src/ListenerApi.Data/Entities/EmployeePayAttributes.cs` — net pay breakdown entity (1:1 with EmployeeRecord)
- `ksqldb/statements.sql` — ksqlDB stream/table definitions for pay period aggregation
- `scripts/seed.sh` — API-based seed script (runs as Docker container, exercises full event pipeline)
- `src/NetPayProcessor/` — Kafka Streams Java app for net pay calculation
- `src/ElasticsearchUpdater/` — Kafka consumer Java app combining employee info + net pay for Elasticsearch
- `docker/Dockerfile.kafka-connect` — Kafka Connect image with Elasticsearch connector
- `src/TransferService.Api/Program.cs` — TransferService DI, MassTransit saga registration
- `src/TransferService.Application/Messages/TransferMessages.cs` — `TransferRequested`, `BalanceAccepted`, `ConfirmationTimedOut`, `RetryBankTransfer`
- `src/TransferService.Api/Sagas/TransferStateMachine.cs` — MassTransit saga for transfer orchestration
- `src/TransferService.Api/Sagas/TransferState.cs` — saga state entity
- `src/TransferService.Api/Consumers/TransferRequestConsumer.cs` — consumes from Kafka, publishes `TransferRequested` / `BalanceAccepted`
- `src/TransferService.Domain/Entities/Transfer.cs` — transfer domain entity
- `src/TransferService.Domain/Entities/BankAccount.cs` — bank account domain entity
- `src/TransferService.Infrastructure/DependencyInjection.cs` — transfer infrastructure registration
- `docker/Dockerfile.transferapi` — TransferService.Api Dockerfile
- `src/ListenerApi.Data/Entities/OutboxMessage.cs` — Debezium outbox entity
- `docs/transfer-outbox-options.md` — Comparison of outbox approaches (MySQL, MassTransit, Debezium)

## Known Issues

- Re-running ksqlDB initialization (via seed or `--profile init`) drops and recreates topics (via `DELETE TOPIC`), which causes the running `net-pay-processor` and `elasticsearch-updater` to lose their source topics. Both auto-recover: they detect the error state, wait 30 seconds for topics to be recreated, then restart their full lifecycle. No manual intervention needed.
