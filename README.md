# Dapr POC - Employee Payroll System

A proof of concept demonstrating Dapr with Kafka and MongoDB using a C# ASP.NET Core Web API themed around employee payroll.

## Architecture

This project follows Domain Driven Design (DDD) principles with the following layers:

- **Domain Layer**: Contains entities, value objects, domain events, and repository interfaces
- **Application Layer**: Contains DTOs, commands, queries, and handlers using MediatR
- **Infrastructure Layer**: Contains MongoDB repositories, Dapr event publishing, and data seeding
- **API Layer**: Contains ASP.NET Core controllers and Swagger documentation

## Features

- **Employee Management**: CRUD operations for employee demographics (salary/hourly with pay rates)
- **Time Clock**: Clock in/out functionality with automatic hours calculation
- **Tax Information**: Federal and state tax withholding configuration
- **Deductions**: Various payroll deductions (health, dental, 401k, etc.)
- **Event-Driven**: All data changes trigger domain events published to Kafka via Dapr
- **Transactional Consistency**: Outbox pattern ensures database and Kafka are always in sync

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

## Services

| Service | Port | Description |
|---------|------|-------------|
| payroll-api | 5000 | Payroll API Service |
| mongodb | 27017 | MongoDB Database |
| kafka | 9092/29092 | Kafka Message Broker |
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

The following topics are created automatically on startup:
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
