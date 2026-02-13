# Dapr POC - Employee Payroll System

A proof of concept demonstrating an event-driven microservices architecture using Dapr, Kafka, MongoDB, MySQL, and GraphQL. A C# ASP.NET Core Web API manages employee payroll data and publishes domain events that are consumed by a downstream listener service, which exposes real-time GraphQL subscriptions to connected clients.

## Architecture

```
Payroll Frontend (React)          Listener Client (React + URQL)
     │                                    │
     ▼                                    ▼ (GraphQL + WebSocket)
Payroll API (ASP.NET Core)        Listener API (ASP.NET Core + HotChocolate)
     │ Dapr Sidecar                       │ Dapr Sidecar
     │                                    │
     ▼                                    ▲
  MongoDB                    Kafka (employee-events)              MySQL
     │                            │                                 ▲
     └────── publish ─────────────┘─────────── consume ─────────────┘
```

**Event Flow**: The Payroll API persists data to MongoDB and publishes domain events to Kafka via its Dapr sidecar. The Listener API consumes those events via its own Dapr sidecar, stores a denormalized read model in MySQL, and pushes real-time updates to the Listener Client over GraphQL subscriptions (WebSocket).

### Payroll API (DDD Layers)

- **Domain Layer**: Entities, value objects, domain events, and repository interfaces
- **Application Layer**: DTOs, commands, queries, and handlers using MediatR
- **Infrastructure Layer**: MongoDB repositories, Dapr event publishing, and data seeding
- **API Layer**: ASP.NET Core controllers and Swagger documentation

### Listener API

- **Data Layer** (`ListenerApi.Data`): EF Core DbContext, repository, event processor, and subscription publisher
- **API Layer** (`ListenerApi`): GraphQL queries, mutations, and subscriptions via HotChocolate

## Features

- **Employee Management**: CRUD operations for employee demographics (salary/hourly with pay rates)
- **Time Clock**: Clock in/out functionality with automatic hours calculation
- **Tax Information**: Federal and state tax withholding configuration
- **Deductions**: Various payroll deductions (health, dental, 401k, etc.)
- **Event-Driven Architecture**: All data changes publish domain events to Kafka via Dapr with full entity payloads
- **GraphQL with Real-Time Subscriptions**: Listener API exposes queries, mutations, and WebSocket subscriptions via HotChocolate
- **CQRS Read Model**: Listener API maintains a separate MySQL read model built from consumed Kafka events
- **Idempotent Event Processing**: Duplicate and out-of-order events are detected and skipped using event ID and timestamp tracking
- **Distributed Tracing**: All Dapr-enabled services report traces to Zipkin at 100% sampling rate

## Frontend Applications

### Payroll Frontend (Port 3000)
React 19 application for managing employee payroll data through the REST API.

- Employee list with filtering and sorting
- Employee detail view with pay info, tax info, deductions, and time entries
- Clock in/out controls
- Sidebar navigation
- Nginx reverse proxy to Payroll API

**Tech**: React 19, Vite, React Router, Axios, Lucide React, date-fns

### Listener Client (Port 3001)
React 19 application for observing real-time employee changes via GraphQL subscriptions.

- **Change Stream**: Live feed of employee changes with connection status indicator, colored change-type badges (created/updated/activated/deactivated), and auto-scrolling
- **Employee Records**: Table view of all denormalized employee records with refresh and delete-all capabilities
- WebSocket-based GraphQL subscriptions via URQL

**Tech**: React 19, Vite, URQL, graphql-ws, Lucide React, date-fns

## Prerequisites

- Docker and Docker Compose
- .NET 7.0 SDK (for local development)
- Node.js 18+ (for frontend development)
- Dapr CLI (optional, for local debugging)

## Quick Start

1. **Start all services**:
   ```bash
   docker-compose up -d
   ```

2. **Access the applications**:
   - Payroll Frontend: http://localhost:3000
   - Listener Client: http://localhost:3001
   - Payroll API Swagger: http://localhost:5000/swagger
   - Listener API GraphQL: http://localhost:5001/graphql

3. **Monitoring**:
   - Kafka UI: http://localhost:8080
   - Zipkin Tracing: http://localhost:9411

## Services

| Service | Port | Description |
|---------|------|-------------|
| frontend | 3000 | Payroll management React app |
| listener-client | 3001 | Real-time GraphQL listener React app |
| payroll-api | 5000 | Payroll REST API (ASP.NET Core + MongoDB) |
| listener-api | 5001 | GraphQL API (ASP.NET Core + HotChocolate + MySQL) |
| payroll-api-dapr | — | Dapr sidecar for payroll-api (HTTP 3500, gRPC 50001) |
| listener-api-dapr | — | Dapr sidecar for listener-api (HTTP 3501, gRPC 50002) |
| mongodb | 27017 | MongoDB — primary payroll data store |
| mysql | 3306 | MySQL — listener read model |
| kafka | 9092/29092 | Kafka message broker |
| zookeeper | 2181 | Zookeeper (Kafka dependency) |
| kafka-ui | 8080 | Kafka topic and message monitoring UI |
| zipkin | 9411 | Distributed tracing |

## API Endpoints

### Payroll REST API (port 5000)

#### Employees
- `GET /api/employees` - Get all employees
- `GET /api/employees/{id}` - Get employee by ID
- `POST /api/employees` - Create employee
- `PUT /api/employees/{id}` - Update employee
- `DELETE /api/employees/{id}` - Deactivate employee

#### Time Entries
- `GET /api/timeentries/employee/{employeeId}` - Get time entries for employee
- `POST /api/timeentries/clock-in/{employeeId}` - Clock in
- `POST /api/timeentries/clock-out/{employeeId}` - Clock out

#### Tax Information
- `GET /api/taxinformation/employee/{employeeId}` - Get tax info
- `POST /api/taxinformation` - Create tax info
- `PUT /api/taxinformation/employee/{employeeId}` - Update tax info

#### Deductions
- `GET /api/deductions/employee/{employeeId}` - Get deductions for employee
- `POST /api/deductions` - Create deduction
- `PUT /api/deductions/{id}` - Update deduction
- `DELETE /api/deductions/{id}` - Deactivate deduction

### Listener GraphQL API (port 5001)

#### Queries
- `employees` - Get all employee records (supports filtering and sorting)
- `employeeById(id: ID!)` - Get a single employee record

#### Mutations
- `deleteAllEmployees` - Delete all employee records (returns count, success, message)

#### Subscriptions
- `onEmployeeChanged` - Real-time stream of employee changes (returns employee data + change type)

## Kafka Topics

Created automatically on startup:
- `employee-events` - Employee create/update/activate/deactivate events
- `timeentry-events` - Clock in/out events
- `taxinfo-events` - Tax information changes
- `deduction-events` - Deduction changes

### Event Payloads

Domain events include full entity data so downstream consumers can build complete records without querying the source:

```json
{
  "eventId": "guid",
  "occurredOn": "2024-01-15T10:30:00Z",
  "eventType": "employee.created",
  "employeeId": "guid",
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@example.com",
  "payType": 0,
  "payRate": 75000.00
}
```

Event types: `employee.created`, `employee.updated`, `employee.deactivated`, `employee.activated`

## Dapr Configuration

### Components (`dapr/components/`)
- **kafka-pubsub.yaml** - Pub/sub for payroll-api (consumer group: `payroll-service-group`)
- **kafka-pubsub-listener.yaml** - Pub/sub for listener-api (consumer group: `listener-api-group`)

### Config (`dapr/config.yaml`)
- Zipkin tracing at 100% sampling rate
- Pub/sub auto-acknowledgment enabled

## Seed Data

MongoDB is seeded with 5 employees on startup:
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

2. **Run the Payroll API with Dapr**:
   ```bash
   cd src/PayrollService.Api
   dapr run --app-id payroll-api --app-port 5000 --dapr-http-port 3500 --components-path ../../dapr/components --config ../../dapr/config.yaml -- dotnet run
   ```

3. **Run the Listener API with Dapr**:
   ```bash
   cd src/ListenerApi
   dapr run --app-id listener-api --app-port 5001 --dapr-http-port 3501 --components-path ../../dapr/components --config ../../dapr/config.yaml -- dotnet run
   ```

4. **Run the frontends** (in separate terminals):
   ```bash
   cd frontend && npm install && npm run dev    # http://localhost:5173
   cd listenerClient && npm install && npm run dev  # http://localhost:3001
   ```

## Project Structure

```
PayrollServicePoc/
├── src/
│   ├── PayrollService.Api/           # Payroll REST API
│   │   ├── Controllers/
│   │   └── Program.cs
│   ├── PayrollService.Application/   # CQRS commands, queries, DTOs
│   │   ├── Commands/
│   │   ├── Queries/
│   │   ├── DTOs/
│   │   └── Interfaces/
│   ├── PayrollService.Domain/        # Domain entities and events
│   │   ├── Entities/
│   │   ├── Events/
│   │   ├── Enums/
│   │   ├── Common/
│   │   └── Repositories/
│   ├── PayrollService.Infrastructure/ # MongoDB, Dapr, seeding
│   │   ├── Persistence/
│   │   ├── Repositories/
│   │   ├── Events/
│   │   └── Seeding/
│   ├── ListenerApi/                  # GraphQL API (HotChocolate)
│   │   ├── GraphQL/
│   │   └── Controllers/
│   └── ListenerApi.Data/            # EF Core + MySQL data layer
│       ├── Entities/
│       ├── Repositories/
│       ├── Services/
│       └── Migrations/
├── frontend/                         # Payroll management UI (React)
│   ├── src/
│   ├── nginx.conf
│   └── Dockerfile
├── listenerClient/                   # Real-time listener UI (React)
│   ├── src/
│   ├── nginx.conf
│   └── Dockerfile
├── dapr/
│   ├── components/
│   │   ├── kafka-pubsub.yaml
│   │   ├── kafka-pubsub-listener.yaml
│   │   └── statestore.yaml
│   └── config.yaml
├── docker/
│   ├── Dockerfile
│   └── Dockerfile.listenerapi
├── docker-compose.yaml
└── PayrollService.sln
```

## Cleanup

```bash
docker-compose down -v
```
