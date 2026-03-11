# Transfer Command Outbox: Option A vs Option B

## Current Implementation (No Outbox — The Gap)

```
ListenerClient
    |
    | POST /api/Transfer
    v
ListenerApi (TransferController)
    |
    |--- Step 1: Save TransferRecord to MySQL ---- SUCCESS (durable)
    |             Status = "Queued"
    |
    |--- Step 2: Publish to Kafka via Dapr -------- FAILURE? (not atomic)
    |             topic: transfer-requests
    |
    v
202 Accepted (client sees "Queued")

PROBLEM: If Step 2 fails, the client sees a "Queued" transfer
         that will never progress. The MySQL write and Kafka
         publish are two independent operations — not atomic.
```

---

## Option A: MySQL Transactional Outbox

ListenerApi uses MySQL as both the command store and the outbox.
A single MySQL transaction writes the TransferRecord AND an outbox
message. A background publisher drains the outbox to Kafka.

### Write Path

```
ListenerClient
    |
    | POST /api/Transfer
    v
ListenerApi (TransferController)
    |
    |  BEGIN MySQL Transaction
    |  |
    |  |--- INSERT TransferRecord (Status = "Queued")
    |  |--- INSERT OutboxMessage   (Topic = "transfer-requests", Payload = {...})
    |  |
    |  COMMIT  <-- atomic, both succeed or both fail
    |
    v
202 Accepted
    |
    |  (client has a durable, repeatable read immediately)
    |
    v
OutboxPublisher (background service in ListenerApi)
    |
    | Polls OutboxMessages table
    | For each unsent message:
    |   1. Publish to Kafka (transfer-requests)
    |   2. Mark message as sent (or delete)
    |
    | Runs on interval (e.g. every 1s)
    | Retries on failure — guaranteed delivery
    |
    v
Kafka (transfer-requests topic)
    |
    v
TransferService (subscribes, processes, emits transfer-events)
    |
    v
ListenerApi (subscribes to transfer-events)
    |
    v
UPDATE TransferRecord in MySQL (Queued -> Initiated -> Completed/Failed)
```

### Data Flow Diagram

```
+------------------+       +-------------------------------------------+
|  ListenerClient  |       |              ListenerApi                  |
|  (Browser)       |       |                                           |
|                  | POST  |  +------------------+  +----------------+ |
|  Transfer Form --+------>|  | TransferRecord   |  | OutboxMessages | |
|                  |       |  | (MySQL)          |  | (MySQL)        | |
|                  | 202   |  |                  |  |                | |
|  <---------------+-------+  | id: abc-123      |  | id: 1          | |
|                  |       |  | status: Queued   |  | topic: xfer-req| |
|                  |       |  | amount: 816.41   |  | payload: {...} | |
|                  |       |  | employee: xyz    |  | sent: false    | |
|                  |       |  +------------------+  +-------+--------+ |
|                  |       |                                |          |
|                  |       |  OutboxPublisher (background)   |          |
|                  |       |     polls unsent messages ------+          |
|                  |       |     publishes to Kafka                     |
|                  |       +------------------+------------------------+
|                  |                          |
|                  |                          v
|                  |                   +-------------+
|                  |                   |    Kafka    |
|                  |                   | transfer-   |
|                  |                   | requests    |
|                  |                   +------+------+
|                  |                          |
|                  |                          v
|                  |               +-------------------+
|                  |               | TransferService   |
|                  |               | (Actor + Workflow) |
|                  |               +--------+----------+
|                  |                        |
|                  |                        v
|                  |                 +-------------+
|                  |                 |    Kafka    |
|                  |                 | transfer-   |
|                  |                 | events      |
|                  |                 +------+------+
|                  |                        |
|                  |       +----------------+-------------------+
|                  |       |  ListenerApi (event subscription)  |
|                  |       |                                    |
|                  |       |  UPDATE TransferRecord             |
|                  |       |    status: Queued -> Initiated     |
|                  |       |    status: Initiated -> Completed  |
|                  |       +------------------------------------+
+------------------+
```

### Pros
- MySQL is already the primary datastore for ListenerApi
- Single technology — no additional Dapr state store needed
- Full control over outbox polling, retry, and dead-letter logic
- TransferRecord table is the source of truth for the client

### Cons
- Requires building the outbox publisher (background service + polling loop)
- Requires a new OutboxMessages table + EF migration
- Polling introduces slight latency (1-2s) before the command reaches Kafka
- Different pattern from PayrollService/TransferService (which use Dapr outbox)

---

## Option B: Dapr State Store Outbox

ListenerApi uses a Dapr state store (backed by MySQL or a separate store)
with the built-in outbox feature. Dapr atomically writes the state entry
AND publishes the outbox message to Kafka. MySQL TransferRecords is
updated best-effort as a read model.

### Write Path

```
ListenerClient
    |
    | POST /api/Transfer
    v
ListenerApi (TransferController)
    |
    |--- Dapr State Store Transaction (atomic) ----------------------+
    |    |                                                            |
    |    |--- Save state: transfer:{id} = {Queued, amount, ...}      |
    |    |--- Outbox metadata publishes to Kafka (transfer-requests)  |
    |    |                                                            |
    |    +------------------------------------------------------------+
    |
    |--- Best-effort: INSERT TransferRecord into MySQL
    |    (if this fails, Dapr state store is still the source of truth)
    |    (the event projection from transfer-events will catch up)
    |
    v
202 Accepted
    |
    | Dapr sidecar handles the Kafka publish — no background service needed
    | The message is part of the state transaction — guaranteed delivery
    |
    v
Kafka (transfer-requests topic)
    |
    v
TransferService (subscribes, processes, emits transfer-events)
    |
    v
ListenerApi (subscribes to transfer-events)
    |
    v
UPSERT TransferRecord in MySQL (Queued -> Initiated -> Completed/Failed)
```

### Data Flow Diagram

```
+------------------+       +-------------------------------------------+
|  ListenerClient  |       |              ListenerApi                  |
|  (Browser)       |       |                                           |
|                  | POST  |  +-----------------------+                |
|  Transfer Form --+------>|  | Dapr State Store      |                |
|                  |       |  | (statestore-listener-  |                |
|                  |       |  |  transfers)            |                |
|                  |       |  |                        |                |
|                  |       |  | key: transfer:abc-123  |                |
|                  |       |  | val: {Queued, 816.41}  |                |
|                  |       |  |                        |                |
|                  |       |  | outbox config:         |                |
|                  |       |  |   pubsub: kafka-pubsub |                |
|                  |       |  |   topic: transfer-req  |  Best-effort  |
|                  |       |  +-----------+------------+  +----------+ |
|                  | 202   |              |               | MySQL    | |
|  <---------------+-------|  Dapr sidecar|               | Transfer | |
|                  |       |  publishes   |               | Records  | |
|                  |       |  atomically  |               |  (read   | |
|                  |       |              |               |   model) | |
|                  |       +-------------------------------------------+
|                  |                      |
|                  |                      v
|                  |               +-------------+
|                  |               |    Kafka    |
|                  |               | transfer-   |
|                  |               | requests    |
|                  |               +------+------+
|                  |                      |
|                  |                      v
|                  |           +-------------------+
|                  |           | TransferService   |
|                  |           | (Actor + Workflow) |
|                  |           +--------+----------+
|                  |                    |
|                  |                    v
|                  |             +-------------+
|                  |             |    Kafka    |
|                  |             | transfer-   |
|                  |             | events      |
|                  |             +------+------+
|                  |                    |
|                  |       +-------------------------------------------+
|                  |       |  ListenerApi (event subscription)          |
|                  |       |                                            |
|                  |       |  UPSERT TransferRecord in MySQL            |
|                  |       |    status: Queued -> Initiated -> Done     |
|                  |       |                                            |
|                  |       |  (catches up even if best-effort write     |
|                  |       |   failed during the POST)                  |
|                  |       +--------------------------------------------+
+------------------+
```

### Pros
- Consistent with PayrollService and TransferService (same Dapr outbox pattern)
- No custom outbox publisher to build — Dapr sidecar handles publish atomically
- Zero latency between state write and Kafka publish (same transaction)
- Already partially wired — `statestore-listener-transfers` constant exists in the code

### Cons
- Requires a Dapr state store component for ListenerApi (YAML config)
- MySQL becomes a best-effort read model — if the best-effort write fails AND
  the event projection hasn't caught up yet, there's a brief window where a
  refresh won't show the transfer (until transfer-events arrives)
- Dapr's outbox stringifies JSON payloads (known issue #8130) — downstream
  consumers must handle this

---

## Comparison

```
                        Option A                    Option B
                        (MySQL Outbox)              (Dapr State Store Outbox)
+--------------------+---------------------------+---------------------------+
| Source of truth     | MySQL                     | Dapr state store          |
| Outbox mechanism    | Custom (polling service)  | Built-in (Dapr sidecar)   |
| Atomicity           | MySQL transaction         | Dapr state transaction    |
| Publish latency     | 1-2s (polling interval)   | ~0s (same transaction)    |
| New infrastructure  | OutboxMessages table      | Dapr component YAML       |
| New code            | OutboxPublisher service   | Minimal (state store call)|
| Pattern consistency | Different from other svcs | Same as Payroll/Transfer  |
| Repeatable read     | Always (MySQL is truth)   | Almost always*            |
| Complexity          | Medium                    | Low                       |
+--------------------+---------------------------+---------------------------+

* In Option B, if the best-effort MySQL write fails, there's a brief gap
  until the transfer-events projection catches up. In practice this is
  sub-second since the Dapr outbox publishes immediately and ListenerApi
  subscribes to transfer-events.
```

---

## Recommendation: Debezium Outbox Pattern

The recommended approach is the **Debezium Outbox Pattern** (also called "Transactional Outbox with CDC"). This is a named, industry-standard pattern that combines the atomicity of Option A (single MySQL transaction) with zero-custom-code publishing via Change Data Capture. Debezium provides a dedicated `outbox-event-router` SMT (Single Message Transform) built specifically for this purpose — it reads the outbox table, routes each row to the correct Kafka topic, and handles cleanup automatically.

### Why This Is Best Practice

- **It has a name.** The "Debezium Outbox Pattern" is a well-documented, widely-recognized approach — not a custom invention. Debezium's official documentation dedicates an entire chapter to it.
- **Battle-tested at scale.** Used in production at Netflix, Airbnb, WePay, Zalando, and others processing millions of events per day.
- **Purpose-built tooling.** Debezium's `outbox-event-router` SMT reads the outbox table, routes to the correct Kafka topic based on the row's `Topic` column, uses `AggregateId` as the Kafka message key (preserving ordering), and sends `Payload` as the message value — all configured declaratively.
- **Atomicity.** A single MySQL transaction covers both the `TransferRecord` (client read model) and the `OutboxMessage` (command to publish). Either both succeed or neither does.
- **No custom outbox publisher.** Debezium runs as a Kafka Connect connector — which is already part of this stack. It tails the MySQL binlog and handles publish, retry, offset tracking, and exactly-once delivery. No background polling service to build or maintain.
- **Low latency.** Binlog tailing is near-real-time (~100ms), not bound by a polling interval like Option A's 1-2 second cycle.
- **Guaranteed repeatable reads.** The client's `TransferRecord` is durable from the moment the MySQL transaction commits. There is no "brief gap" like Option B — MySQL is the source of truth, not a best-effort read model.
- **Minimal runtime dependencies.** The only dependency at request time is MySQL, which ListenerApi already requires. No Dapr sidecar in the critical write path.

### Implementation Overview

```
ListenerApi
    |
    |  BEGIN MySQL Transaction
    |  |--- INSERT TransferRecords  (client read model, Status = "Queued")
    |  |--- INSERT OutboxMessages   (command envelope for Debezium)
    |  COMMIT
    |
    v
202 Accepted (client has durable, repeatable read)

MySQL Binlog
    |
    | Debezium MySQL Source Connector (Kafka Connect)
    | with outbox-event-router SMT
    |   - Detects INSERT on OutboxMessages table
    |   - Routes to Kafka topic specified in the row
    |   - Uses AggregateId as Kafka message key (ordering)
    |   - Sends Payload as message value
    |
    v
Kafka (transfer-requests topic)
    |
    v
TransferService (Actor + Workflow, unchanged)
    |
    v
Kafka (transfer-events topic)
    |
    v
ListenerApi (event subscription, unchanged)
    |
    v
UPDATE TransferRecord in MySQL (Queued -> Initiated -> Completed/Failed)
```

### What Changes

- `src/ListenerApi.Data/Entities/OutboxMessage.cs` — new entity
- `src/ListenerApi.Data/DbContext/ListenerDbContext.cs` — add `OutboxMessages` DbSet
- `src/ListenerApi.Data/Migrations/` — new migration for Outbox table
- `src/ListenerApi/Controllers/TransferController.cs` — single MySQL transaction (TransferRecord + OutboxMessage), remove Dapr pub/sub publish
- `docker/Dockerfile.kafka-connect` — add Debezium MySQL connector plugin
- `docker-compose.yaml` — MySQL binlog configuration for CDC
- `scripts/seed.sh` — register Debezium outbox connector

### Outbox Table Schema

```sql
CREATE TABLE OutboxMessages (
    Id          CHAR(36) PRIMARY KEY,
    AggregateId VARCHAR(255) NOT NULL,    -- employeeId (Kafka message key, for ordering)
    Topic       VARCHAR(255) NOT NULL,    -- "transfer-requests"
    Payload     JSON NOT NULL,            -- the command body
    CreatedAt   DATETIME(6) NOT NULL
);
```

Debezium's outbox router reads `Topic` to decide where to publish, uses `AggregateId` as the Kafka message key (ensuring all transfers for the same employee are ordered), and sends `Payload` as the message value.
