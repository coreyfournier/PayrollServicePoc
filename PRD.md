# Product Requirements Document (PRD)
# PayrollServicePoc — Earned Wage Access Platform

**Version:** 1.0
**Date:** February 2025
**Status:** Hackathon Proof of Concept
**Repository:** https://github.com/coreyfournier/PayrollServicePoc

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Problem Statement](#2-problem-statement)
3. [Product Vision & Goals](#3-product-vision--goals)
4. [System Architecture](#4-system-architecture)
5. [Service Inventory](#5-service-inventory)
6. [Domain Model](#6-domain-model)
7. [API Specifications](#7-api-specifications)
8. [Event-Driven Architecture](#8-event-driven-architecture)
9. [AI Chatbot — Earned Wage Access Assistant](#9-ai-chatbot--earned-wage-access-assistant)
10. [EWA Balance Engine](#10-ewa-balance-engine)
11. [Frontend Applications](#11-frontend-applications)
12. [Data Storage](#12-data-storage)
13. [Infrastructure & Deployment](#13-infrastructure--deployment)
14. [Seed Data](#14-seed-data)
15. [Non-Functional Requirements](#15-non-functional-requirements)
16. [V2 Roadmap](#16-v2-roadmap)
17. [Appendix](#17-appendix)

---

## 1. Executive Summary

PayrollServicePoc is a full-stack, event-driven payroll platform built as a hackathon proof of concept. It demonstrates how modern distributed systems patterns — Domain-Driven Design (DDD), CQRS, event sourcing via Kafka, and AI-powered tool calling — can combine to deliver an **Earned Wage Access (EWA)** experience where employees interact with a conversational AI chatbot to understand and access their earned wages in real time.

The platform comprises **12+ Docker services** spanning a .NET payroll REST API, a GraphQL subscription service, an Anthropic Claude-powered chatbot, two React frontends, MongoDB, MySQL, Kafka, Dapr sidecars, and observability tooling — all orchestrated via Docker Compose.

---

## 2. Problem Statement

### Industry Challenge
Earned Wage Access is a fast-growing employee benefit allowing workers to access a portion of wages already earned before payday. Current solutions suffer from:

- **Opaque calculations** — Employees see a withdrawal limit with no explanation of how it was derived.
- **Support burden** — "Why can I only withdraw $175?" generates high-volume support tickets.
- **Static interfaces** — Traditional EWA apps show a number; they cannot answer follow-up questions.
- **Data fragmentation** — Payroll data, tax withholdings, and deduction schedules live in separate systems with no unified view.

### Technical Challenge
Building a modern payroll platform requires:

- **Event-driven consistency** — Changes in the write model must propagate reliably to read models and downstream consumers.
- **Real-time visibility** — Stakeholders need live feeds of employee and payroll changes.
- **AI integration** — The chatbot must call real APIs with real data, not hallucinate numbers.
- **Polyglot persistence** — Different access patterns demand different storage engines.

---

## 3. Product Vision & Goals

### Vision
An AI-first payroll platform where employees self-serve their EWA inquiries through natural language conversation, backed by real-time payroll data and transparent business rules.

### Goals

| # | Goal | Success Criteria |
|---|------|-----------------|
| 1 | Conversational EWA | Employee asks "How much can I withdraw?" and receives an accurate, explained answer |
| 2 | Transparent calculations | Every withdrawal limit shows gross earned, taxes, deductions, and the limiting rule |
| 3 | Real-time event stream | Employee changes propagate to the listener UI within seconds via Kafka + GraphQL subscriptions |
| 4 | Read-only AI safety | The chatbot can query 6 payroll endpoints but cannot modify any data |
| 5 | Single-command deployment | `docker-compose up` launches the entire platform locally |

---

## 4. System Architecture

### High-Level Data Flow

```
┌──────────────┐     REST      ┌──────────────────┐    Kafka     ┌──────────────────┐
│  Payroll UI  │ ──────────── │   Payroll API     │ ──────────── │   Listener API    │
│  (React)     │  CRUD ops    │  ASP.NET + Dapr   │  Domain      │  GraphQL + MySQL  │
│  Port 3000   │              │  MongoDB + Orleans│  Events      │  Port 5001        │
└──────────────┘              └──────────────────┘              └──────────────────┘
                                       │                                │
                                       │ REST (tool calls)              │ WebSocket
                                       ▼                                ▼
                              ┌──────────────────┐              ┌──────────────────┐
                              │   Chatbot API     │              │  Listener Client  │
                              │  Claude Sonnet    │              │  React + URQL     │
                              │  Port 5002        │              │  Port 3001        │
                              └──────────────────┘              └──────────────────┘
                                       ▲
                                       │ Chat messages
                              ┌──────────────────┐
                              │  Employee (User)  │
                              │  "How much can I  │
                              │   withdraw today?"│
                              └──────────────────┘
```

### Architectural Patterns

| Pattern | Implementation |
|---------|---------------|
| **Domain-Driven Design** | Rich domain entities with private setters, value objects, domain events |
| **CQRS** | MediatR command/query separation in PayrollService.Application |
| **Event Sourcing** | All state changes published as domain events to Kafka via Dapr |
| **Outbox Pattern** | `outbox_messages` MongoDB collection for reliable event publishing |
| **Polyglot Persistence** | MongoDB (write model) + MySQL (read model) |
| **AI Tool Calling** | Claude calls 6 read-only payroll API endpoints via structured tool definitions |
| **GraphQL Subscriptions** | Real-time WebSocket push for employee change events |
| **Sidecar Pattern** | Dapr sidecars for pub/sub abstraction and distributed tracing |

---

## 5. Service Inventory

### Application Services

| Service | Technology | Port | Purpose |
|---------|-----------|------|---------|
| **payroll-api** | ASP.NET Core 7 + MediatR + Orleans | 5000 | Payroll CRUD REST API, domain event publishing |
| **listener-api** | ASP.NET Core + HotChocolate GraphQL | 5001 | Real-time read model with subscriptions |
| **chatbot-api** | ASP.NET Core + Anthropic SDK | 5002 | AI-powered EWA chatbot with tool calling |
| **frontend** | React 19 + Vite + Nginx | 3000 | Payroll management UI |
| **listener-client** | React 19 + Vite + URQL + Nginx | 3001 | Change stream viewer + EWA chat |

### Infrastructure Services

| Service | Technology | Port | Purpose |
|---------|-----------|------|---------|
| **mongodb** | MongoDB 7.0 | 27017 | Primary payroll data store |
| **mysql** | MySQL 8.0 | 3306 | Listener API read model |
| **kafka** | Confluent Kafka 7.5.0 | 9092 | Event broker (4 topics, 3 partitions each) |
| **zookeeper** | Confluent Zookeeper 7.5.0 | 2181 | Kafka coordination |
| **zipkin** | OpenZipkin | 9411 | Distributed tracing (100% sampling) |
| **kafka-ui** | Provectus Kafka UI | 8080 | Kafka topic monitoring |

### Dapr Sidecars

| Sidecar | Attached To | HTTP Port | gRPC Port |
|---------|------------|-----------|-----------|
| **payroll-api-dapr** | payroll-api | 3500 | 50001 |
| **listener-api-dapr** | listener-api | 3501 | 50002 |

---

## 6. Domain Model

### Core Entities

#### Employee
```
Id              : Guid (Primary Key)
FirstName       : string
LastName        : string
Email           : string
PayType         : enum { Hourly = 1, Salary = 2 }
PayRate         : decimal
HireDate        : DateTime
IsActive        : bool
CreatedAt       : DateTime
UpdatedAt       : DateTime
```

#### TimeEntry
```
Id              : Guid (Primary Key)
EmployeeId      : Guid (Foreign Key → Employee)
ClockIn         : DateTime
ClockOut        : DateTime? (nullable until clock-out)
HoursWorked     : decimal (calculated on clock-out)
CreatedAt       : DateTime
UpdatedAt       : DateTime
```

#### TaxInformation
```
Id                              : Guid
EmployeeId                      : Guid (FK → Employee)
FederalFilingStatus             : string (Single | Married | Head of Household)
FederalAllowances               : int
AdditionalFederalWithholding    : decimal
State                           : string (CA, NY, TX, FL, WA, etc.)
StateFilingStatus               : string
StateAllowances                 : int
AdditionalStateWithholding      : decimal
CreatedAt                       : DateTime
UpdatedAt                       : DateTime
```

#### Deduction
```
Id              : Guid
EmployeeId      : Guid (FK → Employee)
DeductionType   : enum { Health=1, Dental=2, Vision=3, Retirement401k=4, LifeInsurance=5, Other=99 }
Description     : string
Amount          : decimal
IsPercentage    : bool (if true, Amount is % of gross)
IsActive        : bool
CreatedAt       : DateTime
UpdatedAt       : DateTime
```

#### EmployeeRecord (Listener Read Model)
```
Id                  : Guid (Primary Key, mirrored from write model)
FirstName           : string
LastName            : string
Email               : string
PayType             : string
PayRate             : decimal?
IsActive            : bool
LastEventType       : string
LastEventTimestamp   : DateTime
LastEventId         : Guid (idempotency tracking)
CreatedAt           : DateTime
UpdatedAt           : DateTime
```

### Domain Events

| Entity | Event Type | Kafka Topic |
|--------|-----------|-------------|
| Employee | `employee.created` | employee-events |
| Employee | `employee.updated` | employee-events |
| Employee | `employee.activated` | employee-events |
| Employee | `employee.deactivated` | employee-events |
| TimeEntry | `timeentry.clockedin` | timeentry-events |
| TimeEntry | `timeentry.clockedout` | timeentry-events |
| TaxInformation | `taxinfo.created` | taxinfo-events |
| TaxInformation | `taxinfo.updated` | taxinfo-events |
| Deduction | `deduction.created` | deduction-events |
| Deduction | `deduction.updated` | deduction-events |
| Deduction | `deduction.deactivated` | deduction-events |

---

## 7. API Specifications

### 7.1 Payroll REST API (Port 5000)

Base URL: `http://localhost:5000/api`
Documentation: `http://localhost:5000/swagger`

#### Employees

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/employees` | List all employees |
| GET | `/employees/{id}` | Get employee by ID |
| POST | `/employees` | Create employee |
| PUT | `/employees/{id}` | Update employee |
| DELETE | `/employees/{id}` | Deactivate employee (soft delete, sets IsActive=false) |

**POST /employees Request Body:**
```json
{
  "firstName": "string",
  "lastName": "string",
  "email": "string",
  "payType": 1,
  "payRate": 75000.00,
  "hireDate": "2024-01-15T00:00:00Z"
}
```

#### Time Entries

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/timeentries/employee/{employeeId}` | Get all time entries for employee |
| POST | `/timeentries/clock-in/{employeeId}` | Clock in (creates entry, sets ClockIn=now) |
| POST | `/timeentries/clock-out/{employeeId}` | Clock out (sets ClockOut=now, calculates HoursWorked) |

#### Tax Information

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/taxinformation/employee/{employeeId}` | Get employee tax configuration |
| POST | `/taxinformation` | Create tax info record |
| PUT | `/taxinformation/employee/{employeeId}` | Update tax info |

#### Deductions

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/deductions/employee/{employeeId}` | Get all deductions for employee |
| POST | `/deductions` | Create deduction |
| PUT | `/deductions/{id}` | Update deduction |
| DELETE | `/deductions/{id}` | Deactivate deduction |

#### Earned Wage Access Balance

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/v1/employees/{employeeId}/balance?includeBreakdown=true` | Calculate EWA balance |

**Response:**
```json
{
  "grossEarnedWages": 1442.31,
  "estimatedTaxes": 499.44,
  "estimatedDeductions": 336.54,
  "netEarnedWages": 606.33,
  "ewaWithdrawal": {
    "availableBalance": 606.33,
    "maxDailyWithdrawal": 200.00,
    "withdrawalLimitPercent": 70,
    "availableToWithdrawToday": 200.00,
    "maxWithdrawalsPerDay": 1,
    "withdrawalsToday": 0,
    "canWithdraw": true
  }
}
```

### 7.2 Listener GraphQL API (Port 5001)

Endpoint: `http://localhost:5001/graphql`
WebSocket: `ws://localhost:5001/graphql`

#### Queries
```graphql
type Query {
  employees: [EmployeeRecord!]!
  employeeById(id: ID!): EmployeeRecord
}
```

#### Mutations
```graphql
type Mutation {
  deleteAllEmployees: DeleteResult!
}

type DeleteResult {
  deletedCount: Int!
  success: Boolean!
  message: String
}
```

#### Subscriptions
```graphql
type Subscription {
  onEmployeeChanged: EmployeeChangedPayload!
}

type EmployeeChangedPayload {
  employee: EmployeeRecord!
  changeType: String!    # employee.created | employee.updated | employee.activated | employee.deactivated
  timestamp: DateTime!
}
```

### 7.3 Chatbot API (Port 5002)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/chat` | Send message, receive AI response |
| GET | `/health` | Health check |

**POST /api/chat Request:**
```json
{
  "message": "How much can I withdraw today?",
  "conversationHistory": [
    { "role": "user", "content": "Hi, I'm John Smith" },
    { "role": "assistant", "content": "Hello John! How can I help?" }
  ]
}
```

**POST /api/chat Response:**
```json
{
  "response": "Based on your current pay period data, here's your EWA balance...",
  "conversationHistory": [
    { "role": "user", "content": "Hi, I'm John Smith" },
    { "role": "assistant", "content": "Hello John! How can I help?" },
    { "role": "user", "content": "How much can I withdraw today?" },
    { "role": "assistant", "content": "Based on your current pay period data..." }
  ]
}
```

---

## 8. Event-Driven Architecture

### Kafka Topics

| Topic | Partitions | Events |
|-------|-----------|--------|
| `employee-events` | 3 | created, updated, activated, deactivated |
| `timeentry-events` | 3 | clockedin, clockedout |
| `taxinfo-events` | 3 | created, updated |
| `deduction-events` | 3 | created, updated, deactivated |

### Event Envelope Format
```json
{
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "occurredOn": "2024-01-15T10:30:00Z",
  "eventType": "employee.created",
  "employeeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@company.com",
  "payType": 2,
  "payRate": 75000.00
}
```

### Event Processing Pipeline

```
Payroll API                    Dapr Sidecar             Kafka              Dapr Sidecar            Listener API
┌──────────┐  domain event  ┌────────────┐  publish  ┌───────┐  deliver  ┌────────────┐  process  ┌──────────┐
│ MediatR  │ ──────────── │ payroll-api│ ───────── │ topic │ ────────── │ listener-  │ ────────── │ Event    │
│ Command  │  outbox       │ -dapr      │           │       │           │ api-dapr   │           │ Processor│
│ Handler  │               └────────────┘           └───────┘           └────────────┘           └──────────┘
└──────────┘                                                                                          │
                                                                                                      ▼
                                                                                              ┌──────────────┐
                                                                                              │ MySQL Upsert │
                                                                                              │ + GraphQL    │
                                                                                              │ Subscription │
                                                                                              │ Broadcast    │
                                                                                              └──────────────┘
```

### Idempotency Guarantees

The Listener API event processor implements dual-check idempotency:

1. **Duplicate detection** — If `eventId == LastEventId`, skip (exact duplicate)
2. **Out-of-order detection** — If `eventTimestamp <= LastEventTimestamp`, skip (stale event)

This ensures at-least-once delivery from Kafka does not corrupt the read model.

---

## 9. AI Chatbot — Earned Wage Access Assistant

### Overview

The chatbot is powered by **Claude Sonnet 4.5** (model: `claude-sonnet-4-5-20250929`) via the Anthropic .NET SDK. It operates as a **read-only** assistant with access to 6 tool definitions that map to payroll API endpoints.

### AI Configuration

| Parameter | Value |
|-----------|-------|
| Model | claude-sonnet-4-5-20250929 |
| Max Tokens | 4,096 |
| Temperature | 0 (deterministic) |
| Max Tool Round-Trips | 10 per conversation turn |

### Tool Definitions

| Tool Name | Required Params | Description |
|-----------|----------------|-------------|
| `get_all_employees` | (none) | List all employees — used to resolve name → ID |
| `get_employee_by_id` | employeeId (GUID) | Full employee profile |
| `get_time_entries` | employeeId (GUID) | Clock in/out records for the employee |
| `get_tax_information` | employeeId (GUID) | Federal & state tax withholding config |
| `get_deductions` | employeeId (GUID) | Health, 401k, dental, vision, etc. |
| `get_ewa_balance` | employeeId (GUID) | Full EWA balance calculation with breakdown |

### Conversation Flow

```
User: "How much can I withdraw today?"
  │
  ├── Claude calls: get_all_employees → finds employee ID by name
  │
  ├── Claude calls: get_ewa_balance(employeeId) → gets full breakdown
  │
  └── Claude responds with formatted breakdown:
        • Gross earned wages: $X,XXX.XX
        • Estimated taxes: -$XXX.XX
        • Estimated deductions: -$XXX.XX
        • Net earned balance: $X,XXX.XX
        • Available to withdraw today: $XXX.XX
        • Explanation of the limiting rule (daily cap or 70% rule)
```

### Safety Rules

- **Read-only access** — If an employee asks to modify data (change tax withholdings, update deductions), the chatbot politely declines and explains it can only view information.
- **Error handling** — If tax information is missing, the API returns a 422 with code `INSUFFICIENT_DATA`. The chatbot explains the balance cannot be calculated and suggests contacting HR.
- **No hallucination** — All monetary figures come from real API calls. Temperature is set to 0.

---

## 10. EWA Balance Engine

### Business Rules

| Rule | Value |
|------|-------|
| Max daily withdrawal | $200.00 |
| Max withdrawal percentage | 70% of net earned wages |
| Withdrawals per day | 1 |
| Available to withdraw | MIN($200, 70% of net earned) |
| Pay period | Biweekly (anchored to Monday, Jan 1, 2024) |

### Calculation Pipeline

```
Step 1: GROSS EARNED WAGES
├── Salary employees: (AnnualRate / 26 pay periods) × (days elapsed / 14 days)
└── Hourly employees: SUM(hoursWorked in current period) × hourlyRate

Step 2: ESTIMATED TAXES
├── Federal: 22% base rate
│   ├── Adjusted by filing status (Married = base × 0.85)
│   ├── Reduced by allowances ($4,300 each × federal rate)
│   └── Plus additional federal withholding
├── State: 5% base rate (varies by state)
│   ├── No-income-tax states: TX, FL, WA, NV, SD, AK, TN, NH = 0%
│   ├── Adjusted by filing status
│   ├── Reduced by allowances ($2,000 each × state rate)
│   └── Plus additional state withholding
└── FICA: 7.65% (Social Security 6.2% + Medicare 1.45%)

Step 3: ESTIMATED DEDUCTIONS
├── Fixed deductions: SUM(amount) for all active, non-percentage deductions
└── Percentage deductions: SUM(gross × amount/100) for all active percentage deductions

Step 4: NET EARNED WAGES
└── Gross - Taxes - Deductions (floored at $0.00)

Step 5: AVAILABLE TO WITHDRAW TODAY
└── MIN($200.00, 70% of net earned)
```

### Example Calculations

| Employee | Pay Type | Gross Earned | Taxes | Deductions | Net Earned | 70% of Net | Available |
|----------|----------|-------------|-------|------------|------------|-----------|-----------|
| John Smith | Salary $75k | ~$2,884.62 | ~$999 | ~$423 | ~$1,462 | ~$1,023 | **$200** (daily cap) |
| Sarah Johnson | Hourly $28.50 (20hrs) | ~$570.00 | ~$197 | ~$34 | ~$339 | ~$237 | **$200** (daily cap) |
| New hire (2 days) | Salary $75k | ~$412.09 | ~$142 | ~$60 | ~$210 | ~$147 | **$147** (70% rule) |

---

## 11. Frontend Applications

### 11.1 Payroll Management UI (Port 3000)

**Technology:** React 19 + Vite + React Router v6 + Axios + Nginx
**Purpose:** Administrative interface for managing employees, time entries, taxes, and deductions.

**Routes:**
| Route | Component | Description |
|-------|-----------|-------------|
| `/` | EmployeeList | Filterable, sortable list of all employees |
| `/employees/:id` | EmployeeDetail | Full employee profile with clock in/out controls |

**Features:**
- Employee CRUD operations
- Clock in / clock out controls
- Tax information management
- Deduction management
- Responsive design with Lucide React icons

**Proxy Configuration (Nginx):**
- `/api/*` → `payroll-api:80` (Payroll REST API)

### 11.2 Listener Client (Port 3001)

**Technology:** React 19 + Vite + URQL + graphql-ws + react-markdown + Nginx
**Purpose:** Real-time change stream viewer and EWA chatbot interface.

**Views:**

| View | Component | Description |
|------|-----------|-------------|
| Change Stream | `ChangeStreamView` | Live feed of employee change events via GraphQL subscriptions |
| Employee Records | `EmployeeRecordsView` | Table of all employee records from the read model |
| EWA Chat | `ChatView` | AI-powered chatbot for EWA inquiries |

**Chat UI Components:**
| Component | File | Purpose |
|-----------|------|---------|
| `ChatView` | `components/ChatView.jsx` | Main container: message history, input bar, quick actions |
| `ChatMessage` | `components/ChatMessage.jsx` | Message bubbles (user right-aligned, assistant left with markdown) |
| `ChatIdentityBar` | `components/ChatIdentityBar.jsx` | Employee identity selector dropdown |

**Chat Features:**
- Employee selector dropdown (5 seeded employees)
- Quick-action chips: "What's my balance?", "Show my deductions", etc.
- Markdown-rendered assistant responses (tables, bullets, headers)
- Typing indicator with bouncing dot animation
- Conversation history management (reset on employee switch)
- Auto-scroll to latest message
- Error state with retry

**Proxy Configuration (Nginx):**
- `/graphql` → `listener-api:80` (GraphQL API)
- `/api/chat` → `chatbot-api:80` (Chatbot API)

---

## 12. Data Storage

### 12.1 MongoDB (Write Model) — Port 27017

**Database:** `payroll`

| Collection | Indexes | Purpose |
|------------|---------|---------|
| `employees` | email (asc) | Employee master records |
| `time_entries` | employeeId (asc) | Clock in/out records |
| `tax_information` | employeeId (asc) | Federal & state tax config |
| `deductions` | employeeId (asc) | Payroll deductions |
| `outbox_messages` | processedAt (asc) | Transactional outbox for reliable event publishing |

### 12.2 MySQL (Read Model) — Port 3306

**Database:** `listenerdb`

| Table | Indexes | Purpose |
|-------|---------|---------|
| `employee_records` | LastEventId, LastEventTimestamp | Denormalized read model for query and subscription |

The read model is populated by the Listener API's event processor consuming Kafka events. It maintains idempotency via `LastEventId` and `LastEventTimestamp` fields.

---

## 13. Infrastructure & Deployment

### Docker Compose Deployment

All services launch with a single command:
```bash
docker-compose up -d --build
```

### Dapr Configuration

**Pub/Sub Components:**

| Component | Attached Service | Consumer Group |
|-----------|-----------------|----------------|
| `kafka-pubsub` | payroll-api | payroll-service-group |
| `kafka-pubsub-listener` | listener-api | listener-api-group |

**Global Dapr Config:**
- Tracing: 100% sampling rate → Zipkin
- Feature: `pubsub.autoack` enabled

### Environment Configuration

The chatbot-api requires an Anthropic API key injected via `.env` file:

```
ANTHROPIC_API_KEY=sk-ant-api03-...
```

The `docker-compose.yaml` uses `env_file: .env` on the chatbot-api service (rather than shell variable interpolation, which has compatibility issues on Windows).

### Service URLs (Local Development)

| Service | URL |
|---------|-----|
| Payroll UI | http://localhost:3000 |
| Listener Client (Chat) | http://localhost:3001 |
| Payroll API Swagger | http://localhost:5000/swagger |
| GraphQL Playground | http://localhost:5001/graphql |
| Kafka UI | http://localhost:8080 |
| Zipkin Tracing | http://localhost:9411 |

---

## 14. Seed Data

The Payroll API seeds 5 mock employees on startup:

| Name | Pay Type | Rate | Hire Date | Deductions |
|------|----------|------|-----------|------------|
| John Smith | Salary | $75,000/yr | 2020-01-15 | Health ($250), 401k (6%) |
| Sarah Johnson | Hourly | $28.50/hr | 2021-03-20 | Health, 401k |
| Michael Williams | Salary | $85,000/yr | 2019-06-01 | Health, 401k |
| Emily Brown | Hourly | $32.00/hr | 2022-09-10 | Health, 401k |
| David Davis | Salary | $95,000/yr | 2018-11-05 | Health, 401k |

All employees receive:
- Random tax information (federal/state filing status, allowances)
- Health insurance and 401k deductions (first 3 employees guaranteed)

---

## 15. Non-Functional Requirements

### Performance
- Chatbot response time: < 5 seconds (dependent on Claude API latency)
- GraphQL subscription latency: < 2 seconds from event publish to UI update
- Payroll API response time: < 200ms for standard CRUD operations

### Security (V1 Limitations)
- **No authentication** — Employee identity is selected via dropdown (V2: SSO)
- **Read-only AI** — Chatbot cannot modify any payroll data
- **API key protection** — Anthropic key stored in `.env` file, not committed to source control

### Reliability
- Kafka provides durable message storage with at-least-once delivery
- Outbox pattern ensures events are published even if Kafka is temporarily unavailable
- Idempotent event processing prevents duplicate updates to the read model
- GraphQL WebSocket auto-reconnects with exponential backoff (10 retries)

### Observability
- **Distributed tracing** — Zipkin captures 100% of traces across all services
- **Structured logging** — All services use structured logging with correlation
- **Kafka monitoring** — Kafka UI provides topic inspection, consumer lag visibility

---

## 16. V2 Roadmap

| Feature | Priority | Description |
|---------|----------|-------------|
| Real authentication | High | SSO/OAuth instead of name dropdown |
| Withdrawal execution | High | Actually process EWA transfers, not just calculate |
| Push notifications | Medium | "Your balance just increased" via GraphQL subscriptions |
| Conversation persistence | Medium | Save chat history across sessions (MongoDB) |
| Multi-language support | Medium | Claude handles this natively |
| Audit logging | High | Every balance inquiry and withdrawal logged for compliance |
| Withdrawal history tracking | High | Track actual withdrawals to enforce daily limits |
| Mobile-responsive chat | Medium | Optimize chat UI for mobile devices |
| Rate limiting | Medium | Protect chatbot API from abuse |
| Employee self-service portal | Low | Unified dashboard: balance, pay stubs, deductions, chat |

---

## 17. Appendix

### A. Files Created/Modified for EWA Chat Feature

**New Files:**
| File | Purpose |
|------|---------|
| `listenerClient/src/components/ChatView.jsx` | Main chat container with message history and input |
| `listenerClient/src/components/ChatMessage.jsx` | Message bubbles with markdown rendering |
| `listenerClient/src/components/ChatIdentityBar.jsx` | Employee identity selector dropdown |
| `listenerClient/src/api/chat.js` | HTTP client for chatbot API |
| `src/ChatbotApi/Services/EwaBalanceCalculator.cs` | EWA balance calculation engine |

**Modified Files:**
| File | Change |
|------|--------|
| `listenerClient/src/App.jsx` | Added "EWA Chat" tab |
| `listenerClient/src/index.css` | Added chat UI styles (~327 lines) |
| `listenerClient/vite.config.js` | Added `/api/chat` proxy for development |
| `listenerClient/nginx.conf` | Added `/api/chat` proxy to chatbot-api for production |
| `listenerClient/package.json` | Added react-markdown, remark-gfm dependencies |
| `src/ChatbotApi/Services/ChatService.cs` | Enhanced system prompt with EWA rules |
| `src/ChatbotApi/Tools/ToolDefinitions.cs` | Added `get_ewa_balance` tool definition |
| `src/ChatbotApi/Tools/ToolExecutor.cs` | Wired EWA balance calculator to tool executor |
| `src/ChatbotApi/Program.cs` | Registered IEwaBalanceCalculator in DI container |
| `docker-compose.yaml` | Added env_file for Anthropic API key |

### B. State Tax Rate Reference

| State | Rate | Notes |
|-------|------|-------|
| TX, FL, WA, NV, SD, AK, TN, NH | 0% | No state income tax |
| CA | 6% | Highest in system |
| NY | 5.5% | |
| All others | 4-5% | Default base rate |

### C. Glossary

| Term | Definition |
|------|-----------|
| **EWA** | Earned Wage Access — the ability to withdraw earned wages before payday |
| **CQRS** | Command Query Responsibility Segregation — separate models for reads and writes |
| **DDD** | Domain-Driven Design — modeling software around business domains |
| **Dapr** | Distributed Application Runtime — sidecar for pub/sub, service invocation |
| **Orleans** | Microsoft virtual actor framework for stateful distributed computing |
| **MediatR** | .NET library for in-process messaging (commands, queries, notifications) |
| **HotChocolate** | GraphQL server library for .NET |
| **URQL** | Lightweight GraphQL client for React |
| **Tool Calling** | AI pattern where the LLM requests structured API calls to gather real data |
