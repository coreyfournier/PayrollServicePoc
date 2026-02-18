# Dapr POC - Employee Payroll System

A proof of concept demonstrating Dapr with Kafka and MongoDB using a C# ASP.NET Core Web API themed around employee payroll.

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

## Features

- **Employee Management**: CRUD operations for employee demographics (salary/hourly with pay rates)
- **Time Clock**: Clock in/out functionality with automatic hours calculation
- **Tax Information**: Federal and state tax withholding configuration
- **Deductions**: Various payroll deductions (health, dental, 401k, etc.)
- **Event-Driven**: All data changes trigger domain events published to Kafka via Dapr
- **Transactional Consistency**: Dapr state store is the authoritative write path — entity state and outbox events are written atomically. MongoDB collections serve as a best-effort read model updated after the Dapr transaction succeeds.

## Prerequisites

- Docker and Docker Compose
- .NET 7.0 SDK (for local development)
- Dapr CLI (optional, for local debugging)

## Quick Start

1. **Start all services**:
   ```bash
   docker-compose up -d
   ```

2. **Access the API**:
   - Swagger UI: http://localhost:5000/swagger
   - API Base URL: http://localhost:5000/api

3. **View distributed traces**:
   - Zipkin: http://localhost:9411

## Listener API & Listener Client

The **Listener API** (`src/ListenerApi`) is a .NET 7.0 GraphQL server (HotChocolate) backed by MySQL. It subscribes to the `employee-events` Kafka topic via Dapr and persists employee records to its own database, demonstrating an event-driven read model. Events are processed idempotently using timestamp comparison. It exposes:

- **GraphQL queries** — fetch employee records from MySQL
- **GraphQL mutations** — manage records (e.g., delete all)
- **GraphQL subscriptions** — real-time WebSocket notifications when employee data changes

The **Listener Client** (`listenerClient/`) is a React + Vite frontend that connects to the Listener API using [urql](https://github.com/urql-graphql/urql) and `graphql-ws`. It provides two views:

- **Change Stream** — a live feed of employee changes pushed via GraphQL WebSocket subscriptions in real time
- **Employee Records** — a queryable list of all employee records stored in the Listener API's MySQL database

Together, they demonstrate an end-to-end event-driven pipeline: REST API mutation → domain event → Kafka → Dapr subscription → MySQL projection → GraphQL subscription → real-time UI update.

## Services

| Service | Port | Description |
|---------|------|-------------|
| payroll-api | 5000 | Payroll API Service |
| listener-api | 5001 | GraphQL Listener API |
| frontend | 3000 | React frontend (REST client) |
| listener-client | 3001 | React frontend (GraphQL subscription client) |
| mongodb | 27017 | MongoDB Database |
| mysql | 3306 | MySQL Database (Listener API) |
| kafka | 9092/29092 | Kafka Message Broker |
| kafka-ui | 8080 | Kafka monitoring UI |
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
| `employee-net-pay` | NetPayProcessor | Net pay breakdown per employee per pay period (gross - taxes - deductions). Compacted topic (`cleanup.policy=compact,delete`) |

Additional internal topics managed by ksqlDB (created/dropped by `ksqldb-init`):

| Topic | Description |
|-------|-------------|
| `TIME_ENTRY_EVENTS` | Filtered clock-out and time entry update events extracted from `employee-events` |
| `GROSS_PAY_EVENTS` | Combined employee and time entry events normalized for gross pay calculation |
| `employee-net-pay-by-period` | Materialized view of the `employee-net-pay` topic, queryable via ksqlDB pull queries |

## ksqlDB Stream Processing

ksqlDB processes the `employee-events` Kafka topic through a pipeline of streams and tables defined in `ksqldb/statements.sql`. The `ksqldb-init` container executes these statements on startup.

### Streams

| Object | Source | Description |
|--------|--------|-------------|
| `EMPLOYEE_EVENTS_RAW` | `employee-events` topic | Base stream over the raw CloudEvent envelope. `data` is VARCHAR (not STRUCT) because Dapr's outbox stringifies the JSON payload. Fields are extracted via `EXTRACTJSONFIELD` with PascalCase names |
| `TIME_ENTRY_EVENTS` | `EMPLOYEE_EVENTS_RAW` | Filtered for `timeentry.clockedout` and `timeentry.updated` events. Extracts time entry ID, employee ID, hours worked, and computes a bi-weekly pay period number from the clock-in timestamp |
| `GROSS_PAY_EVENTS` | `EMPLOYEE_EVENTS_RAW` | Captures both employee events (pay rate/type changes) and time entry events. Normalizes employee ID via `COALESCE($.EmployeeId, $.Id)`. Uses `'__PAY_RATE__'` sentinel for employee events so they contribute 0 hours in the downstream dedup |

### Tables

| Object | Type | Output Topic | Description |
|--------|------|------------|-------------|
| `EMPLOYEE_HOURS_BY_PERIOD` | Aggregation | `payperiod-hours-changed` | Total hours per employee per pay period. Uses `AS_MAP(COLLECT_LIST(id), COLLECT_LIST(hours))` to deduplicate by time entry ID (last value wins), then `REDUCE(MAP_VALUES(...))` sums the latest hours. This prevents double-counting when time entries are edited |
| `EMPLOYEE_GROSS_PAY_BY_PERIOD` | Aggregation | `employee-gross-pay` | Gross pay per employee per pay period. Tracks pay rate via `LATEST_BY_OFFSET(PAY_RATE, true)` (ignores nulls from time entry events). For hourly employees (`PayType=1`): rate x summed hours. For salaried employees (`PayType=2`): annual rate / 2080 x `PayPeriodHours` |
| `EMPLOYEE_NET_PAY_BY_PERIOD` | Source | `employee-net-pay` | Read-only materialized view over the compacted `employee-net-pay` topic produced by NetPayProcessor. Queryable via pull queries. Tombstones from NetPayProcessor automatically delete rows for deactivated employees |

## Seed Data

The database is seeded with 5 mock employees on startup:
1. John Smith (Salary - $75,000/year)
2. Sarah Johnson (Hourly - $28.50/hour)
3. Michael Williams (Salary - $85,000/year)
4. Emily Brown (Hourly - $32.00/hour)
5. David Davis (Salary - $95,000/year)

Each employee has associated tax information and some have deductions configured.

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

## Project Structure

```
DaprPoc/
├── src/
│   ├── PayrollService.Api/           # API Layer
│   │   ├── Controllers/
│   │   └── Program.cs
│   ├── PayrollService.Application/   # Application Layer
│   │   ├── Commands/
│   │   ├── Queries/
│   │   ├── DTOs/
│   │   └── Interfaces/
│   ├── PayrollService.Domain/        # Domain Layer
│   │   ├── Entities/
│   │   ├── Events/
│   │   ├── Enums/
│   │   ├── Common/
│   │   └── Repositories/
│   └── PayrollService.Infrastructure/ # Infrastructure Layer
│       ├── Persistence/
│       ├── Repositories/
│       ├── Events/
│       └── Seeding/
├── dapr/
│   ├── components/
│   │   ├── kafka-pubsub.yaml
│   │   └── statestore.yaml
│   └── config.yaml
├── docker/
│   └── Dockerfile
├── docker-compose.yaml
└── PayrollService.sln
```

## Cleanup

```bash
docker-compose down -v
```

5. **Connect to MongoDB with Compass**:
   ```
   mongodb://localhost:27017/?directConnection=true
   ```
   The `directConnection=true` parameter is required because the MongoDB container runs as a replica set with the Docker hostname `mongodb`. Without it, Compass attempts to resolve the replica set member hostname and fails with `getaddrinfo ENOTFOUND`.
