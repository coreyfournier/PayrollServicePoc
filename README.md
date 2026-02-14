# Employee Payroll System

An event-driven microservices proof of concept built with C# ASP.NET Core, demonstrating Domain-Driven Design with Kafka event streaming, Orleans virtual actors, MongoDB/MySQL persistence, and real-time GraphQL subscriptions.

## Architecture

Two independent backend APIs communicate via Kafka events:

- **Payroll API** (.NET 8.0): REST API following DDD + CQRS patterns with MediatR. Uses Microsoft Orleans for stateful virtual actor grains with MongoDB persistence. Publishes domain events directly to Kafka via Confluent.Kafka.
- **Listener API** (.NET 7.0): Subscribes to Kafka employee events via a Dapr sidecar. Persists events to MySQL using EF Core with idempotent upserts. Exposes a GraphQL API (HotChocolate) with real-time WebSocket subscriptions.
- **Frontend** (React 19 + Vite): Payroll management UI for employee CRUD, time clock, tax info, and deductions.
- **Listener Client** (React 19 + Vite): Real-time employee change stream viewer using GraphQL subscriptions via URQL + graphql-ws.

## Prerequisites

- Docker and Docker Compose
- .NET 8.0 SDK (Payroll API) and .NET 7.0 SDK (Listener API) for local development
- Node.js 18+ (for frontend development)
- Dapr CLI (optional, for running Listener API locally)

## Quick Start

1. **Start all services**:
   ```bash
   docker-compose up -d
   ```

2. **Access the applications**:
   - Frontend: http://localhost:3000
   - Listener Client: http://localhost:3001

3. **Access the APIs**:
   - Payroll REST API: http://localhost:5000/api
   - Swagger UI: http://localhost:5000/swagger
   - GraphQL Playground: http://localhost:5001/graphql
   - GraphQL WebSocket: ws://localhost:5001/graphql

4. **Monitoring**:
   - Kafka UI: http://localhost:8080
   - Zipkin Tracing: http://localhost:9411

## Services

| Service | Container | Port | Description |
|---------|-----------|------|-------------|
| payroll-api | payroll-api | 5000 | REST API with Orleans silo (.NET 8.0) |
| frontend | payroll-frontend | 3000 | Payroll management UI (React + Nginx) |
| listener-api | listener-api | 5001 | GraphQL API consuming Kafka events (.NET 7.0) |
| listener-api-dapr | listener-api-dapr | — | Dapr sidecar for Listener API pub/sub |
| listener-client | listener-client | 3001 | Real-time change stream UI (React + Nginx) |
| kafka | kafka | 9092, 29092 | Kafka message broker (Confluent 7.5.0) |
| zookeeper | zookeeper | 2181 | Kafka coordination |
| kafka-init | kafka-init | — | One-shot topic creation |
| kafka-ui | kafka-ui | 8080 | Kafka monitoring UI |
| mongodb | mongodb | 27017 | Payroll data + Orleans grain state (Mongo 7.0, replica set) |
| mysql | mysql | 3306 | Listener API event store (MySQL 8.0) |
| zipkin | zipkin | 9411 | Distributed tracing |

## Payroll API Endpoints

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

## Listener API (GraphQL)

Endpoint: `http://localhost:5001/graphql`

### Queries
- `getEmployees` - Get all employee records (supports filtering and sorting)
- `getEmployeeById(id)` - Get a single employee record

### Mutations
- `deleteAllEmployees` - Delete all employee records

### Subscriptions
- `onEmployeeChanged` - Real-time stream of employee changes via WebSocket

## Kafka Topics

Created automatically on startup (3 partitions each):
- `employee-events` - Employee create/update/deactivate events
- `timeentry-events` - Clock in/out events
- `taxinfo-events` - Tax information changes
- `deduction-events` - Deduction changes

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
   docker-compose up -d zookeeper kafka kafka-init mongodb mysql zipkin
   ```

2. **Run Payroll API locally**:
   ```bash
   cd src/PayrollService.Api
   dotnet run
   ```

3. **Run Listener API with Dapr**:
   ```bash
   cd src/ListenerApi
   dapr run --app-id listener-api --app-port 5001 --components-path ../../dapr/components --config ../../dapr/config.yaml -- dotnet run
   ```

4. **Run frontends**:
   ```bash
   cd frontend && npm install && npm run dev
   cd listenerClient && npm install && npm run dev
   ```

5. **Connect to MongoDB with Compass**:
   ```
   mongodb://localhost:27017/?directConnection=true
   ```
   The `directConnection=true` parameter is required because the MongoDB container runs as a replica set with the Docker hostname `mongodb`. Without it, Compass attempts to resolve the replica set member hostname and fails with `getaddrinfo ENOTFOUND`.

## Project Structure

```
PayrollServicePoc/
├── src/
│   ├── PayrollService.Api/             # REST API + Orleans silo (.NET 8.0)
│   ├── PayrollService.Application/     # MediatR commands, queries, DTOs
│   ├── PayrollService.Domain/          # Entities, domain events, repository interfaces
│   ├── PayrollService.Infrastructure/  # Orleans grains, Kafka publisher, MongoDB, seeding
│   ├── ListenerApi/                    # GraphQL API + Dapr event subscription (.NET 7.0)
│   └── ListenerApi.Data/              # EF Core DbContext, migrations, repositories
├── frontend/                           # Payroll management UI (React + Vite)
├── listenerClient/                     # Change stream viewer (React + Vite)
├── docker/                             # Dockerfiles for API services
├── dapr/                               # Dapr component and tracing configuration
├── docker-compose.yaml
└── PayrollService.sln
```

## Cleanup

```bash
docker-compose down -v
```
