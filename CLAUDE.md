# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Employee payroll system POC demonstrating Dapr with Kafka pub/sub, MongoDB state store, and the transactional outbox pattern. Two independent frontends consume the API: a REST+React app and a GraphQL+WebSocket subscription client.

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

### No test suite exists yet.

## Architecture

### DDD Layers (PayrollService.*)

```
Api (.NET 9.0)  →  Application (.NET 7.0)  →  Domain (.NET 7.0)
                                                      ↑
                                            Infrastructure (.NET 7.0)
```

- **Domain**: Entities (`Employee`, `TimeEntry`, `TaxInformation`, `Deduction`), domain events, repository interfaces. Base `Entity` class collects domain events in-memory.
- **Application**: MediatR CQRS — commands for writes, queries for reads, DTOs for API boundaries.
- **Infrastructure**: MongoDB persistence, Dapr state store integration, event publishing, data seeding. Contains `DependencyInjection.cs` for all service registration.
- **Api**: ASP.NET Core controllers, Swagger UI at `/swagger`.

### Two Unit-of-Work Implementations

The `Features__UseDaprOutbox` env var (default `true` in docker-compose) toggles between:

1. **DaprStateStoreUnitOfWork** — writes entity state + events atomically via Dapr state store's built-in outbox, which auto-publishes to Kafka.
2. **TransactionalUnitOfWork** — writes entity + outbox messages to MongoDB in a transaction, then publishes via `DaprEventPublisher` separately.

Both live in `src/PayrollService.Infrastructure/StateStore/` and `Persistence/`.

### Event Flow

```
Controller → MediatR Handler → Entity (raises domain events)
  → UnitOfWork.ExecuteAsync() → Dapr State Store (outbox) → Kafka
  → ListenerApi (Dapr topic subscription) → MySQL → GraphQL subscription → ListenerClient
```

### ListenerApi (.NET 7.0)

Separate service: HotChocolate GraphQL server backed by MySQL (Pomelo EF Core). Subscribes to Kafka `employee-events` topic via Dapr. Processes events idempotently (checks `LastEventTimestamp`). Broadcasts changes to WebSocket subscribers via in-memory `ITopicEventSender`. Auto-applies EF Core migrations on startup.

### Service Ports (Docker)

| Service | Port | Notes |
|---------|------|-------|
| payroll-api | 5000 | Swagger at /swagger |
| listener-api | 5001 | GraphQL at /graphql |
| frontend | 3000 | REST client |
| listener-client | 3001 | GraphQL client |
| kafka | 9092 (internal), 29092 (host) | |
| kafka-ui | 8080 | |
| mongodb | 27017 | Replica set, connect with `?directConnection=true` |
| mysql | 3306 | |
| zipkin | 9411 | Distributed tracing |

### Dapr Components (`dapr/components/`)

- `statestore-mongodb.yaml` — MongoDB state store with outbox config (`outboxPublishPubsub: kafka-pubsub`, `outboxPublishTopic: employee-events`)
- `kafka-pubsub.yaml` / `kafka-pubsub-listener.yaml` — Kafka pub/sub for payroll-api and listener-api respectively

### Kafka Topics

`employee-events`, `timeentry-events`, `taxinfo-events`, `deduction-events` — created by `kafka-init` container.

### MongoDB

Runs as a single-node replica set (`rs0`) to support multi-document transactions. Replica set is auto-initialized via the container healthcheck script.

### Key Files

- `src/PayrollService.Api/Program.cs` — DI setup, feature flag for Dapr outbox toggle
- `src/PayrollService.Infrastructure/DependencyInjection.cs` — all infrastructure service registration
- `src/PayrollService.Infrastructure/StateStore/DaprStateStoreUnitOfWork.cs` — atomic outbox logic
- `src/PayrollService.Domain/Common/Entity.cs` — base entity with domain event collection
- `src/ListenerApi/Program.cs` — GraphQL schema, Dapr subscription, migration runner
- `dapr/components/statestore-mongodb.yaml` — outbox configuration (critical for event publishing)

## Known Issue

Dapr's transactional outbox does not preserve the data payload as a JSON object — it gets stringified. Tracked at https://github.com/dapr/dapr/issues/8130.
