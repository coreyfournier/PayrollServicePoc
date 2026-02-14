# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Full stack (all services + infrastructure)
docker-compose up -d
docker-compose down -v   # teardown with volume cleanup

# Infrastructure only (for local .NET development)
docker-compose up -d zookeeper kafka kafka-init mongodb mysql zipkin

# Build .NET solution
dotnet build PayrollService.sln
dotnet restore PayrollService.sln

# Run Payroll API locally (requires infrastructure running)
dotnet run --project src/PayrollService.Api

# Run Listener API with Dapr sidecar
dapr run --app-id listener-api --app-port 5001 --components-path ../../dapr/components --config ../../dapr/config.yaml -- dotnet run --project src/ListenerApi

# Frontend development
cd frontend && npm install && npm run dev        # port 5173 (dev), 3000 (docker)
cd listenerClient && npm install && npm run dev  # port 5173 (dev), 3001 (docker)
```

No test projects exist in the solution currently.

## Architecture

Event-driven microservices POC for employee payroll management. Two independent backend APIs communicate via Kafka events.

### Payroll API (src/PayrollService.*) — .NET 8.0, port 5000
REST API using DDD + CQRS pattern. Layers:
- **Domain**: Entities (`Employee`, `TimeEntry`, `TaxInformation`, `Deduction`) with domain events, repository interfaces. Entities use private setters + factory methods + `AddDomainEvent` pattern.
- **Application**: MediatR command/query handlers, DTOs. Commands mutate state and publish domain events; queries are read-only.
- **Infrastructure**: Microsoft Orleans 8.0 virtual actor grains for stateful caching, MongoDB persistence for grain state, `KafkaEventPublisher` publishes events directly to Kafka (Confluent.Kafka). `DataSeeder` populates 5 mock employees on startup.
- **Api**: ASP.NET Core controllers, Swagger at `/swagger`. Hosts the Orleans silo.

### Listener API (src/ListenerApi*) — .NET 7.0, port 5001
GraphQL API (HotChocolate 13.9) consuming Kafka events via Dapr pub/sub sidecar:
- **ListenerApi**: Dapr `EventSubscriptionController` receives events, GraphQL queries/mutations/subscriptions for employee records.
- **ListenerApi.Data**: EF Core with MySQL (Pomelo provider), code-first migrations auto-applied at startup. `EventProcessor` converts CloudEvents to `EmployeeRecord`. Idempotency via `LastTimestamp` — older messages are ignored. `InMemorySubscriptionPublisher` broadcasts changes to GraphQL WebSocket subscribers.

### Frontend Apps — React 19 + Vite
- **frontend/**: Payroll management UI (CRUD, time clock, tax, deductions). Axios for REST calls.
- **listenerClient/**: Real-time employee change stream viewer. URQL + graphql-ws for GraphQL subscriptions.

### Event Flow
1. Payroll API command handler modifies entity → domain event raised
2. `KafkaEventPublisher` sends CloudEvents-formatted message to topic (`employee-events`, `timeentry-events`, `taxinfo-events`, `deduction-events`) — each has 3 partitions
3. Dapr sidecar delivers event to Listener API's `EventSubscriptionController`
4. `EventProcessor` upserts `EmployeeRecord` in MySQL, publishes to in-memory subscription
5. GraphQL subscribers receive real-time notification via WebSocket

### Key Infrastructure
- **Kafka**: Confluent 7.5.0 with Zookeeper. Topics auto-created by `kafka-init` container. Kafka-UI at port 8080.
- **MongoDB**: 7.0 with replica set (transactional support). Orleans grain state storage.
- **MySQL**: 8.0 for Listener API event store.
- **Dapr**: 1.13.0 sidecar for Listener API pub/sub only. Config in `dapr/components/` and `dapr/config.yaml`.
- **Zipkin**: Distributed tracing at port 9411.

## Project Framework Versions

| Project | Target Framework |
|---------|-----------------|
| PayrollService.Api, Domain, Application, Infrastructure | .NET 8.0 |
| ListenerApi, ListenerApi.Data | .NET 7.0 |

## Docker

Dockerfiles are in `docker/` (not project roots):
- `docker/Dockerfile` — PayrollService.Api (aspnet:8.0)
- `docker/Dockerfile.listenerapi` — ListenerApi (aspnet:7.0)
- `frontend/Dockerfile`, `listenerClient/Dockerfile` — Node 18 build → nginx

All services run on `payroll-network` bridge network and include health checks.

## Configuration

- Payroll API: MongoDB connection in `appsettings.json` (`MongoDB:ConnectionString`, `MongoDB:DatabaseName`), Kafka brokers via `Kafka:Brokers` env var
- Listener API: MySQL connection string in `ConnectionStrings:DefaultConnection`
- Docker overrides via environment variables in `docker-compose.yaml`
- Dapr components: `dapr/components/kafka-pubsub.yaml` (producer), `dapr/components/kafka-pubsub-listener.yaml` (consumer)
