# Integration Tests & CI Pipeline Design

## Goal

Add integration tests that catch issues unit tests cannot: DI wiring failures, real database constraint enforcement, outbox atomicity, and Kafka streaming pipeline correctness. Run these automatically on code merge/PR via GitHub Actions.

## Design Principles

- **Domain isolation** — each bounded context tested independently with its own databases
- **Kafka stubbed in domain tests** — messaging replaced with test doubles that capture events
- **Kafka tested separately** — dedicated pipeline test validates NetPayProcessor + ksqlDB produce correct output
- **Testcontainers for databases** — no docker-compose needed for domain tests; containers managed in-process
- **Standard runners where possible** — only the Kafka pipeline test may need larger runners

---

## CI Workflow Structure

### 1. `ci-unit.yml` (renamed from existing `ci.yml`)

- **Triggers:** PR and push to master
- **Behavior:** Unchanged — .NET build/test (PayrollService.UnitTests + TransferService.UnitTests) + frontend lint/test
- **Runners:** `ubuntu-latest`

### 2. `ci-integration.yml`

- **Triggers:** PR and push to master
- **Three parallel jobs:**
  - `payroll-db-tests` — runs `dotnet test tests/PayrollService.DatabaseTests/`
  - `transfer-db-tests` — runs `dotnet test tests/TransferService.DatabaseTests/`
  - `listener-db-tests` — runs `dotnet test tests/ListenerApi.DatabaseTests/`
- **Runners:** `ubuntu-latest` (Testcontainers manages lightweight DB containers)
- **Required SDKs:** .NET 9.0 (can build .NET 7.0 targets via implicit targeting)

### 3. `ci-kafka-pipeline.yml`

- **Triggers:** Push to master only (not PRs — heavier workload)
- **Single job:**
  1. Start services via `docker compose -f docker-compose.kafka-test.yml up -d`
  2. Wait for health checks
  3. Run ksqlDB initialization (submit statements.sql)
  4. Run `dotnet test tests/KafkaPipeline.Tests/`
  5. Tear down
- **Runners:** `ubuntu-latest` (may need larger runner if memory constrained)

---

## Test Project 1: `tests/PayrollService.DatabaseTests/`

**Framework:** xUnit, .NET 9.0, Testcontainers.MongoDb

**Infrastructure:**
- MongoDB standalone via `Testcontainers` (standalone suffices — these tests don't use multi-document transactions)
- Real `PayrollService.Infrastructure` DI registrations (repositories, MongoDbContext)
- Kafka/MassTransit stubbed — `IUnitOfWork` replaced with a test double that captures domain events without publishing
- Mock `IProducer<string, string>` registered to satisfy `MassTransitUnitOfWork`'s transitive dependency (needed for DI resolution test)

**Test cases:**

| Test | What it catches |
|------|----------------|
| DI resolution — all infrastructure services resolve (with mock producer) | Missing registrations, circular dependencies |
| Employee create + retrieve | Repository wiring, MongoDB serialization |
| Employee update + retrieve | Upsert idempotency (ReplaceOne with IsUpsert) |
| TimeEntry create + retrieve | Repository wiring |
| TimeEntry update (same ID) | Upsert overwrites, not duplicates |
| TaxInformation create + update | Repository wiring |
| Deduction create + update | Repository wiring |
| Domain events captured on entity creation | Events raised and collected correctly |

**Fixture:**
- `IAsyncLifetime` class fixture starts MongoDB container once per test class
- Registers real infrastructure services via `PayrollService.Infrastructure.DependencyInjection`
- Registers mock `IProducer<string, string>` for DI resolution
- Replaces `IUnitOfWork` with test double for CRUD tests

---

## Test Project 2: `tests/TransferService.DatabaseTests/`

**Framework:** xUnit, .NET 9.0, Testcontainers.MongoDb

**Infrastructure:**
- MongoDB standalone via Testcontainers (standalone supports partial unique indexes — no replica set needed)
- Real `TransferService.Infrastructure` DI registrations (repositories, TransferMongoDbContext)
- Must call `TransferMongoDbContext.InitializeAsync()` in fixture setup to create the partial unique index (`unique_employee_in_progress_transfer`)
- Stubs for external services: mock `IBankTransferService`, mock `IBalanceService`, mock `ITransferEventPublisher`
- Mock `IProducer<string, string>` registered to satisfy `TransferEventPublisher`'s transitive dependency (needed for DI resolution test, same pattern as PayrollService)
- Configure `IOptions<TransferLimitsOptions>` in test fixture with known values (e.g., MaxPerPayPeriod=5, MaxAmountPerPayPeriod=10000, MaxPerDay=1) — normally bound from config in Program.cs

**Test cases:**

| Test | What it catches |
|------|----------------|
| DI resolution — all infrastructure services resolve (with mocked externals) | Missing registrations, circular dependencies |
| Transfer create + retrieve | Repository wiring, MongoDB serialization |
| One-in-progress transfer constraint — second transfer rejected | MongoDB partial unique index enforcement |
| One-in-progress constraint — completed transfer allows new one | Index only filters non-terminal statuses |
| BankAccount create + retrieve | Repository wiring |
| BankAccount ownership validation | TransferValidationService against real DB |
| TransferValidationService — rejects non-existent bank account | Validation logic with real repository |
| TransferValidationService — rejects wrong employee's account | Cross-employee ownership check |
| TransferValidationService — duplicate in-progress check (excludes current ID) | HasInProgressTransferAsync logic |
| EmployeeTransferLimits CRUD | IEmployeeTransferLimitsRepository wiring |

**Fixture:**
- `IAsyncLifetime` class fixture starts MongoDB container
- Registers real infrastructure services via `TransferService.Infrastructure.DependencyInjection`
- Replaces `IBankTransferService`, `IBalanceService`, `ITransferEventPublisher` with mocks
- Configures `TransferLimitsOptions` via in-memory configuration
- Calls `TransferMongoDbContext.InitializeAsync()` to create indexes

---

## Test Project 3: `tests/ListenerApi.DatabaseTests/`

**Framework:** xUnit, .NET 9.0, Testcontainers.MySql, Pomelo.EntityFrameworkCore.MySql

**Infrastructure:**
- MySQL via Testcontainers
- Real `ListenerDbContext` with EF Core migrations applied (including outbox cleanup event)
- Tests the Debezium outbox write path by exercising `ListenerDbContext` directly (replicating the atomic write pattern from `TransferController`) rather than instantiating the controller, which has HTTP and repository dependencies unrelated to outbox testing

**Test cases:**

| Test | What it catches |
|------|----------------|
| EF Core migrations apply cleanly | Migration correctness, schema validity |
| TransferRecord + OutboxMessage written atomically | MySQL transaction commits both or neither |
| OutboxMessage has correct Topic field | Routing to correct Kafka topic |
| OutboxMessage has correct AggregateId | Per-employee message ordering |
| OutboxMessage Payload is well-formed JSON | Serialization correctness |
| TransferRecord retrievable after write | EF Core mapping, MySQL serialization |

**Fixture:**
- `IAsyncLifetime` class fixture starts MySQL container
- Applies EF Core migrations via `ListenerDbContext.Database.MigrateAsync()`
- Tests exercise the outbox write path directly (or via a minimal service extraction from TransferController)

---

## Test Project 4: `tests/KafkaPipeline.Tests/`

**Framework:** xUnit, .NET 9.0, Confluent.Kafka

**Infrastructure (via `docker-compose.kafka-test.yml`):**

| Service | Purpose |
|---------|---------|
| zookeeper | Kafka dependency |
| kafka | Message broker |
| ksqldb-server | Stream processing |
| net-pay-processor | Kafka Streams app under test |

No .NET API services, no databases, no Elasticsearch.

**Test flow:**

1. Produce CloudEvent-wrapped events to `employee-events` topic:
   - Employee created (salary, $75,000/year, CA, married)
   - Tax info (married filing, CA state)
   - Deductions (health $100 fixed, 401k 5%)
2. Consume from `employee-net-pay` topic (with timeout)
3. Assert calculated values with rounding tolerance (NetPayProcessor uses Java double arithmetic):
   - Gross pay ~= $75,000 / 26 (~$2,884.62)
   - Federal tax matches progressive bracket calculation (within tolerance)
   - State tax ~= 9.3% of annualized gross / 26
   - Deductions ~= $100 + (5% of gross)
   - Net pay ~= gross - taxes - deductions
4. Consume from `employee-info` topic (ksqlDB output)
5. Assert latest employee state is correct

**Additional scenario — hourly employee:**
1. Produce employee (hourly, $28.50/hr) + time entries (40hrs)
2. Assert gross ~= $28.50 * 40 = $1,140.00
3. Produce additional time entry (8hrs), assert recalculated gross ~= $28.50 * 48

**Assertion strategy:** Use `Assert.InRange` or a custom tolerance helper (e.g., within $0.02) rather than exact equality, to account for Java floating-point arithmetic in NetPayProcessor.

**docker-compose.kafka-test.yml:**
- Minimal subset of main docker-compose.yaml
- Hardcoded topic creation in an init container (no seed script dependency)
- ksqlDB statements submitted by the CI job before tests run

---

## File Structure

```
.github/workflows/
  ci-unit.yml                          # renamed from ci.yml
  ci-integration.yml                   # database integration tests
  ci-kafka-pipeline.yml                # Kafka pipeline tests

tests/
  PayrollService.DatabaseTests/
    PayrollService.DatabaseTests.csproj
    Fixtures/
      MongoDbFixture.cs                # Testcontainers MongoDB setup
    TestDoubles/
      TestUnitOfWork.cs                # Captures domain events, no Kafka
    EmployeeCrudTests.cs
    TimeEntryCrudTests.cs
    TaxInformationCrudTests.cs
    DeductionCrudTests.cs
    DependencyInjectionTests.cs

  TransferService.DatabaseTests/
    TransferService.DatabaseTests.csproj
    Fixtures/
      MongoDbFixture.cs                # Testcontainers MongoDB + index init
    TestDoubles/
      MockBankService.cs
      MockBalanceService.cs
      MockTransferEventPublisher.cs
    TransferCrudTests.cs
    TransferConstraintTests.cs
    BankAccountTests.cs
    TransferValidationTests.cs
    TransferLimitsRepositoryTests.cs
    DependencyInjectionTests.cs

  ListenerApi.DatabaseTests/
    ListenerApi.DatabaseTests.csproj
    Fixtures/
      MySqlFixture.cs                  # Testcontainers MySQL + migrations
    OutboxAtomicityTests.cs
    OutboxMessageTests.cs
    MigrationTests.cs

  KafkaPipeline.Tests/
    KafkaPipeline.Tests.csproj
    Fixtures/
      KafkaFixture.cs                  # docker-compose lifecycle
    Helpers/
      CloudEventProducer.cs            # Produces properly formatted events
      TopicConsumer.cs                  # Consumes with timeout + deserialization
    NetPayProcessorTests.cs
    KsqlDbEmployeeInfoTests.cs

docker-compose.kafka-test.yml           # Minimal Kafka stack for pipeline tests
```

---

## Dependencies (NuGet packages)

| Package | Projects | Purpose |
|---------|----------|---------|
| `Testcontainers.MongoDb` | PayrollService.DatabaseTests, TransferService.DatabaseTests | MongoDB containers |
| `Testcontainers.MySql` | ListenerApi.DatabaseTests | MySQL container |
| `Confluent.Kafka` | KafkaPipeline.Tests | Kafka producer/consumer |
| `Pomelo.EntityFrameworkCore.MySql` | ListenerApi.DatabaseTests | EF Core MySQL provider |
| `Microsoft.Extensions.DependencyInjection` | All | DI container for resolution tests |
| `Moq` or `NSubstitute` | PayrollService.DatabaseTests, TransferService.DatabaseTests | Mock external dependencies |
| `xunit` + `xunit.runner.visualstudio` | All | Test framework |

---

## Out of Scope

- Elasticsearch testing (deferred)
- Modifying existing `PayrollService.IntegrationTests` (re-evaluate later)
- Saga state machine integration testing (covered by existing unit tests)
- GraphQL/ListenerApi consumer integration testing
- Self-hosted runner setup
