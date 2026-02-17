# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Employee payroll system POC demonstrating Dapr with Kafka pub/sub, MongoDB state store, the transactional outbox pattern, and ksqlDB stream processing for real-time pay period aggregation. Two independent frontends consume the API: a REST+React app and a GraphQL+WebSocket subscription client.

## Common Commands

### Full stack (Docker)
```bash
docker-compose up -d              # Start everything
docker-compose down -v            # Tear down with volumes
docker-compose up -d zookeeper kafka kafka-init mongodb zipkin  # Infrastructure only
```

### Backend build (local)
```bash
dotnet build PayrollService.sln
dotnet run --project src/PayrollService.Api
```

### Run API with Dapr sidecar (local)
```bash
cd src/PayrollService.Api
dapr run --app-id payroll-api --app-port 5000 --dapr-http-port 3500 \
  --components-path ../../dapr/components --config ../../dapr/config.yaml -- dotnet run
```

### Frontend (REST client)
```bash
cd frontend && npm install && npm run dev    # Dev server
cd frontend && npm run build                 # Production build
cd frontend && npm run lint                  # ESLint
```

### ListenerClient (GraphQL client)
```bash
cd listenerClient && npm install && npm run dev
```

### Seed data
```bash
docker-compose up seed              # Run after the stack is up
```

Creates 5 employees, 40 time entries (for the 2 hourly employees), 5 tax records, and 7 deductions via the REST API so the full event pipeline is exercised (Dapr outbox → Kafka → ksqlDB → ListenerApi → GraphQL). The script (`scripts/seed.sh`) clears existing data first, making it safe to re-run. Requires `payroll-api` and `listener-api` to be healthy.

### No test suite exists yet.

## Architecture

### DDD Layers (PayrollService.*)

```
Api (.NET 9.0)  →  Application (.NET 7.0)  →  Domain (.NET 7.0)
                                                      ↑
                                            Infrastructure (.NET 7.0)
```

- **Domain**: Entities (`Employee`, `TimeEntry`, `TaxInformation`, `Deduction`), domain events, repository interfaces. Base `Entity` class collects domain events in-memory. `Employee.PayPeriodHours` (decimal, default 40) specifies hours per pay period for salaried employees; used by ksqlDB to calculate gross pay instead of time entries.
- **Application**: MediatR CQRS — commands for writes, queries for reads, DTOs for API boundaries.
- **Infrastructure**: MongoDB persistence, Dapr state store integration, event publishing, data seeding. Contains `DependencyInjection.cs` for all service registration.
- **Api**: ASP.NET Core controllers, Swagger UI at `/swagger`.

### Two Unit-of-Work Implementations

The `Features__UseDaprOutbox` env var (default `true` in docker-compose) toggles between:

1. **DaprStateStoreUnitOfWork** — Dapr state store is the authoritative write path. `ExecuteAsync` writes entity state + outbox events atomically via the Dapr state store transaction FIRST, then updates the MongoDB collection as a best-effort read model. If the Dapr transaction fails, nothing is written (consistent). If the MongoDB write fails, a warning is logged but the request succeeds (entity is safely in Dapr state store).
2. **TransactionalUnitOfWork** — writes entity + outbox messages to MongoDB in a transaction, then publishes via `DaprEventPublisher` separately.

Both live in `src/PayrollService.Infrastructure/StateStore/` and `Persistence/`.

### Write Path (DaprStateStoreUnitOfWork)

```
Controller → MediatR Handler → Entity (raises domain events)
  → DaprStateStoreUnitOfWork.ExecuteAsync()
      1. Dapr State Store Transaction  (entity + outbox — ATOMIC, SOURCE OF TRUTH) → Kafka
      2. MongoDB Collection Write      (read model — BEST-EFFORT, logged on failure)
  → ListenerApi (Dapr topic subscription) → MySQL → GraphQL subscription → ListenerClient
```

Repository `AddAsync` methods use `ReplaceOneAsync` with `IsUpsert = true` to be idempotent — retries after a Dapr success don't produce duplicate-key errors in MongoDB.

### ListenerApi (.NET 7.0)

Separate service: HotChocolate GraphQL server backed by MySQL (Pomelo EF Core). Subscribes to Kafka `employee-events` and `employee-net-pay` topics via Dapr. Processes events idempotently (checks `LastEventTimestamp` for employees, `PayPeriodNumber` for pay attributes). Broadcasts changes to WebSocket subscribers via in-memory `ITopicEventSender`. Auto-applies EF Core migrations on startup.

**Entities:**
- `EmployeeRecord` — employee data from `employee-events` topic
- `EmployeePayAttributes` — 1:1 with `EmployeeRecord`, stores latest pay period net pay breakdown from `employee-net-pay` topic. PK = `EmployeeId` (FK to `EmployeeRecord`, cascade delete). Exposed via GraphQL as `payAttributes` nested field on employees.

### Service Ports (Docker)

| Service | Port | Notes |
|---------|------|-------|
| payroll-api | 5000 | Swagger at /swagger |
| listener-api | 5001 | GraphQL at /graphql |
| frontend | 3000 | REST client |
| listener-client | 3001 | GraphQL client |
| kafka | 9092 (internal), 29092 (host) | |
| kafka-ui | 8080 | Also has ksqlDB query UI |
| ksqldb-server | 8088 | REST API |
| mongodb | 27017 | Replica set, connect with `?directConnection=true` |
| mysql | 3306 | |
| zipkin | 9411 | Distributed tracing |

### Dapr Components (`dapr/components/`)

- `statestore-mongodb.yaml` — MongoDB state store with outbox config (`outboxPublishPubsub: kafka-pubsub`, `outboxPublishTopic: employee-events`)
- `kafka-pubsub.yaml` / `kafka-pubsub-listener.yaml` — Kafka pub/sub for payroll-api and listener-api respectively

### Dapr Sidecars

Both Dapr sidecars (`payroll-api-dapr`, `listener-api-dapr`) run as separate containers on `payroll-network` with `-app-channel-address` pointing at their app's service name. This ensures Docker DNS resolves the app across container restarts. **Do not use `network_mode: "service:..."` for sidecars** — it shares the app's network namespace, so when the app restarts the sidecar loses DNS and Kafka connectivity permanently until manually restarted.

### Kafka Topics

`employee-events`, `timeentry-events`, `taxinfo-events`, `deduction-events`, `payperiod-hours-changed`, `employee-gross-pay`, `employee-net-pay` — created by `kafka-init` container. Additional internal topics (`TIME_ENTRY_EVENTS`, `GROSS_PAY_EVENTS`, `employee-net-pay-by-period`) are managed by ksqlDB.

### ksqlDB Stream Processing

ksqlDB processes the `employee-events` Kafka topic to produce per-employee, per-pay-period hour aggregates on the `payperiod-hours-changed` topic. Defined in `ksqldb/statements.sql`, executed by the `ksqldb-init` container on startup.

**Pipeline (7 objects):**

```
employee-events topic
  → EMPLOYEE_EVENTS_RAW stream (raw CloudEvent envelope, data as VARCHAR)
  ├→ TIME_ENTRY_EVENTS stream (filtered for timeentry.clockedout / timeentry.updated)
  │   → EMPLOYEE_HOURS_BY_PERIOD table (aggregated per employee + pay period → payperiod-hours-changed topic)
  └→ GROSS_PAY_EVENTS stream (employee + timeentry events, normalized fields)
      → EMPLOYEE_GROSS_PAY_BY_PERIOD table (rate × hours per employee + pay period → employee-gross-pay topic)

employee-net-pay topic (produced by NetPayProcessor)
  → EMPLOYEE_NET_PAY stream (keyed by employeeId + payPeriodNumber)
      → EMPLOYEE_NET_PAY_BY_PERIOD table (latest net pay per employee + pay period → employee-net-pay-by-period topic)
```

**Key design decisions:**

- **`data` is VARCHAR, not STRUCT** — Dapr's outbox stringifies the JSON payload (known issue #8130). Fields are extracted via `EXTRACTJSONFIELD(data, '$.FieldName')` with PascalCase entity field names (`$.Id`, `$.EmployeeId`, `$.ClockIn`, `$.ClockOut`, `$.HoursWorked`, `$.PayPeriodHours`).
- **Event type filtering** — The top-level CloudEvent `type` is always `com.dapr.event.sent`. The actual event type is extracted from `$.DomainEvents[0].EventType` inside the stringified data.
- **Edit-safe aggregation** — `AS_MAP(COLLECT_LIST(TIME_ENTRY_ID), COLLECT_LIST(HOURS_WORKED))` deduplicates by time entry ID (last value wins for duplicate keys), then `REDUCE(MAP_VALUES(...))` sums the latest hours per entry. This prevents double-counting when time entries are edited.
- **Pay period math** — Bi-weekly periods starting from epoch 2024-01-01T00:00:00Z (1704067200000 ms), each 14 days (1209600000 ms).

**`payperiod-hours-changed` topic schema:**

- Key (JSON): `{"EMPLOYEE_ID": "...", "PAY_PERIOD_NUMBER": 55}`
- Value (JSON): `{"TOTAL_HOURS_WORKED": 12.0, "EVENT_COUNT": 4, "PAY_PERIOD_START": "2026-02-09T00:00:00", "PAY_PERIOD_END": "2026-02-23T00:00:00"}`

**`employee-gross-pay` topic schema:**

- Key (JSON): `{"EMPLOYEE_ID": "...", "PAY_PERIOD_NUMBER": 55}`
- Value (JSON): `{"PAY_RATE": 28.5, "PAY_TYPE": "1", "PAY_PERIOD_HOURS": 40.0, "EFFECTIVE_HOURLY_RATE": 28.5, "TOTAL_HOURS_WORKED": 12.0, "GROSS_PAY": 342.0, "PAY_PERIOD_START": "2026-02-09T00:00:00", "PAY_PERIOD_END": "2026-02-23T00:00:00", "EVENT_COUNT": 5}`

**Gross pay design decisions:**

- **Single-stream approach** — Both employee events (with `PayRate`) and time entry events (with `HoursWorked`) flow through the same `employee-events` topic. `GROSS_PAY_EVENTS` captures both, normalizing `EMPLOYEE_ID` via `COALESCE($.EmployeeId, $.Id)`.
- **Pay rate tracking** — `LATEST_BY_OFFSET(PAY_RATE, true)` ignores nulls from time entry events, keeping the most recent rate from employee events. The `__PAY_RATE__` sentinel for `TIME_ENTRY_ID` contributes 0 hours to the AS_MAP dedup.
- **Salary vs Hourly** — `PayType=1` (Hourly): rate is $/hour. `PayType=2` (Salary): rate is $/year, divided by 2080 (52 weeks × 40 hours) to get the effective hourly rate.
- **PayPeriodHours for salaried employees** — Salaried employees don't clock in/out. The `PayPeriodHours` field on the Employee entity (default 40) specifies how many hours per pay period a salaried employee works. The ksqlDB pipeline uses `LATEST_BY_OFFSET(PAY_PERIOD_HOURS, true)` for `TOTAL_HOURS_WORKED` when `PAY_TYPE = '2'`, instead of summing time entries. Hourly employees still use the AS_MAP+REDUCE dedup pattern.
- **Current period only** — Pay rate changes are assigned to the current pay period (via `$.UpdatedAt`), so only that period's `GROSS_PAY` updates. Past periods retain their existing values.

**Init container behavior:** The `ksqldb-init` container terminates all running queries before executing DROP/CREATE statements, making it safe for re-deploys.

**Querying ksqlDB:**
- Kafka UI at http://localhost:8080 (KSQL DB tab in sidebar)
- CLI: `docker exec -it ksqldb-server ksql http://localhost:8088`
- REST: `curl http://localhost:8088/ksql -H 'Content-Type: application/vnd.ksql.v1+json' -d '{"ksql": "SHOW TABLES;"}'`

### Net Pay Processor (Kafka Streams, Java 17)

Standalone Kafka Streams application (`src/NetPayProcessor/`) that computes per-employee, per-pay-period net pay by combining gross pay with tax configuration and deductions. Connects directly to Kafka (no Dapr sidecar needed).

**Pipeline:**

```
employee-gross-pay topic ──┐
                           ├──→ Kafka Streams App ──→ employee-net-pay topic
employee-events topic ─────┘    (net-pay-processor)
  (taxinfo.*, deduction.*)
```

**Internal topology** (Processor API with state stores):
- `gross-pay-store` — keyed by `employeeId:payPeriod`, latest gross pay from ksqlDB
- `tax-config-store` — keyed by `employeeId`, latest tax config (filing status, state, additional withholding)
- `deduction-store` — keyed by `employeeId`, map of `deductionId → {amount, isPercentage, isActive}`

When a **gross pay event** arrives: update store → look up tax & deductions → compute net pay → emit.
When a **tax info event** arrives: update store → look up latest gross pay for current period → recompute → emit.
When a **deduction event** arrives: update store → look up latest gross pay for current period → recompute → emit.

**Tax calculation:**
- **Federal**: Progressive brackets (2024 rates) — annualizes bi-weekly gross × 26, applies brackets, divides by 26. Single/HeadOfHousehold use single brackets; Married uses married brackets.
- **State**: Simplified flat rates per state (e.g., CA=9.3%, NY=6.85%, TX=0%, WA=0%, IL=4.95%).
- **Additional withholding**: `AdditionalFederalWithholding` and `AdditionalStateWithholding` from tax info events added on top.

**Deduction calculation:**
- **Fixed** (`isPercentage=false`): subtracted directly
- **Percentage** (`isPercentage=true`): `(amount/100) × grossPay`
- **Inactive** (`isActive=false`): contribute $0 (kept in map for reactivation)

**`employee-net-pay` topic schema:**
- Key (JSON): `{"employeeId": "...", "payPeriodNumber": 55}`
- Value (JSON): `{"employeeId": "...", "payPeriodNumber": 55, "grossPay": 4440.0, "federalTax": 170.77, "stateTax": 0.0, "additionalFederalWithholding": 50.0, "additionalStateWithholding": 25.0, "totalTax": 245.77, "totalFixedDeductions": 225.0, "totalPercentDeductions": 0.0, "totalDeductions": 225.0, "netPay": 3969.23, "payRate": 28.5, "payType": "1", "totalHoursWorked": 155.75, "payPeriodStart": "2026-02-09T00:00:00", "payPeriodEnd": "2026-02-23T00:00:00"}`

**`employee-net-pay-by-period` topic schema (ksqlDB materialized view):**
- Key (JSON): `{"EMPLOYEEID": "...", "PAYPERIODNUMBER": 55}`
- Value (JSON): `{"GROSS_PAY": 4440.0, "FEDERAL_TAX": 170.77, "STATE_TAX": 0.0, "ADDITIONAL_FEDERAL_WITHHOLDING": 50.0, "ADDITIONAL_STATE_WITHHOLDING": 25.0, "TOTAL_TAX": 245.77, "TOTAL_FIXED_DEDUCTIONS": 225.0, "TOTAL_PERCENT_DEDUCTIONS": 0.0, "TOTAL_DEDUCTIONS": 225.0, "NET_PAY": 3969.23, "PAY_RATE": 28.5, "PAY_TYPE": "1", "TOTAL_HOURS_WORKED": 155.75, "PAY_PERIOD_START": "2026-02-09T00:00:00", "PAY_PERIOD_END": "2026-02-23T00:00:00", "EVENT_COUNT": 5}`
- Queryable via pull query: `SELECT * FROM EMPLOYEE_NET_PAY_BY_PERIOD WHERE EMPLOYEEID = '...' AND PAYPERIODNUMBER = 55;`

**Key files:**
- `src/NetPayProcessor/pom.xml` — Maven project (kafka-streams, jackson)
- `src/NetPayProcessor/src/main/java/com/payroll/netpay/NetPayApp.java` — topology + main
- `src/NetPayProcessor/src/main/java/com/payroll/netpay/NetPayProcessor.java` — unified processor
- `src/NetPayProcessor/src/main/java/com/payroll/netpay/TaxCalculator.java` — progressive bracket + state tax logic

### MongoDB

Runs as a single-node replica set (`rs0`) to support multi-document transactions. Replica set is auto-initialized via the container healthcheck script.

### Key Files

- `src/PayrollService.Api/Program.cs` — DI setup, feature flag for Dapr outbox toggle
- `src/PayrollService.Infrastructure/DependencyInjection.cs` — all infrastructure service registration
- `src/PayrollService.Infrastructure/StateStore/DaprStateStoreUnitOfWork.cs` — atomic outbox logic
- `src/PayrollService.Domain/Common/Entity.cs` — base entity with domain event collection
- `src/ListenerApi/Program.cs` — GraphQL schema, Dapr subscription, migration runner
- `src/ListenerApi.Data/Entities/EmployeePayAttributes.cs` — net pay breakdown entity (1:1 with EmployeeRecord)
- `src/ListenerApi/Controllers/EventSubscriptionController.cs` — Dapr subscription endpoints (employee-events, employee-net-pay)
- `dapr/components/statestore-mongodb.yaml` — outbox configuration (critical for event publishing)
- `ksqldb/statements.sql` — ksqlDB stream/table definitions for pay period aggregation
- `scripts/seed.sh` — API-based seed script (runs as Docker container, exercises full event pipeline)
- `src/NetPayProcessor/` — Kafka Streams Java app for net pay calculation

## Known Issues

- Dapr's transactional outbox does not preserve the data payload as a JSON object — it gets stringified. Tracked at https://github.com/dapr/dapr/issues/8130. The ksqlDB pipeline works around this by declaring `data` as VARCHAR and using `EXTRACTJSONFIELD`.
- The `COLLECT_LIST` in the ksqlDB aggregation grows unboundedly (appends every event). Acceptable for a POC but would need a retention strategy in production.
- Restarting `ksqldb-init` drops and recreates topics (via `DELETE TOPIC`), which crashes the running `net-pay-processor` with `MissingSourceTopicException`. After an `ksqldb-init` restart, manually restart `net-pay-processor`: `docker restart net-pay-processor`.
