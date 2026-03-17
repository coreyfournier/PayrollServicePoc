# Transfer Usage Aggregation — Requirements

## Problem

Transfer limits (per pay period count, per pay period amount, per day count) must be enforced based on **completed** transfers only. Currently, limits are checked by querying MongoDB at validation time and counting all non-Failed transfers, which incorrectly includes in-progress transfers (Initiated, Processing, AwaitingConfirmation).

## Goals

1. Track how many transfers an employee has completed per pay period and the total amount transferred
2. Track how many transfers an employee has completed per day
3. Only count transfers with status `Completed` — in-progress and failed transfers do not consume limit budget
4. Expose these counts as real-time aggregates for use during transfer validation
5. Follow existing architecture patterns (ksqlDB stream processing, pull queries)

## Current State

### Transfer Statuses
| Status | Counts toward limits? |
|--------|----------------------|
| Initiated | No |
| Processing | No |
| AwaitingConfirmation | No |
| Failed | No |
| **Completed** | **Yes** |

### Existing Limits Configuration
- `MaxPerPayPeriod` — max number of completed transfers per employee per pay period (default: 5)
- `MaxAmountPerPayPeriod` — max total amount of completed transfers per employee per pay period (default: $10,000)
- `MaxPerDay` — max number of completed transfers per employee per day (default: 1)
- Per-employee overrides stored in MongoDB (`employee_transfer_limits` collection)

### Current Enforcement Points
1. **TransferValidationService** (transfer-api, authoritative) — queries MongoDB `transfers` collection
2. **ListenerApi TransferController** (best-effort pre-check) — queries MySQL `TransferRecords` table
3. **TransferLimits value object** — pure validation logic, receives counts/amounts as input

### Current Data Flow
```
Transfer completes → MongoDB write → RabbitMQ TransferUpdated
  → TransferKafkaBridgeConsumer → Kafka (transfer-events) → ListenerApi → MySQL
```

## Proposed Aggregation

### Data Source
The `transfer-events` Kafka topic receives a CloudEvent-wrapped Transfer entity snapshot at every state change. Each transfer reaches `Completed` exactly once.

### Aggregates Needed

**1. Per Pay Period (keyed by employee + pay period number)**
- `TRANSFER_COUNT` — number of completed transfers
- `TOTAL_AMOUNT` — sum of completed transfer amounts

**2. Per Day (keyed by employee + date)**
- `TRANSFER_COUNT` — number of completed transfers

### Technology
ksqlDB stream processing — consistent with existing net pay aggregation pattern:
- Stream from `transfer-events` topic (parse CloudEvent envelope)
- Filter for `Status = 'Completed'`
- Materialized tables for pull queries

### Query Pattern
Pull queries from transfer-api (same pattern as `KsqlDbBalanceService`):
```sql
SELECT TRANSFER_COUNT, TOTAL_AMOUNT
FROM TRANSFER_USAGE_BY_PERIOD
WHERE EMPLOYEE_ID = '...' AND PAY_PERIOD_NUMBER = 57;

SELECT TRANSFER_COUNT
FROM TRANSFER_USAGE_BY_DAY
WHERE EMPLOYEE_ID = '...' AND TRANSFER_DATE = '2026-03-16';
```

## Concurrency Considerations

- The "one in-progress transfer per employee" check remains unchanged (MongoDB unique index + soft check). This prevents concurrent transfer abuse regardless of limit counts.
- ksqlDB aggregates are eventually consistent — there is a small window after a transfer completes where the aggregate may not yet reflect the new count. For a POC this is acceptable. The MongoDB unique index remains the hard concurrency guard.

## Open Questions

1. Should the ListenerApi best-effort check also switch to ksqlDB, or remain on MySQL?
2. Should the daily limit count be based on `InitiatedAt` (when the transfer was started) or `CompletedAt` (when it finished)?
3. If ksqlDB is unavailable, should validation fail open (allow the transfer) or fail closed (reject it)?
4. Should the aggregation handle the edge case of duplicate `Completed` events from at-least-once delivery in the bridge consumer?
