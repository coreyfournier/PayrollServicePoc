# Earned Wage Access (EWA) Chatbot — Hackathon Demo

## Presentation Script (5-10 minutes)

---

## SLIDE 1: Title (30 seconds)

**Say:**

> "Today we're going to show you an AI-powered Earned Wage Access platform we built during this hackathon. The core idea is simple: employees should be able to ask a chatbot, in plain English, 'How much can I withdraw today?' — and get an instant, accurate answer based on their real payroll data."

---

## SLIDE 2: The Problem (45 seconds)

**Say:**

> "Earned Wage Access is a fast-growing benefit where employees can access a portion of wages they've already earned before payday. But there are challenges:"

- Employees don't understand how their available balance is calculated
- They don't know why their withdrawal limit is what it is
- Traditional EWA apps show a number with no context
- Support tickets pile up: 'Why can I only withdraw $175?'

> "We asked: what if an AI assistant could explain all of this conversationally — pulling from the same payroll data your system already has?"

---

## SLIDE 3: Architecture Overview (1 minute)

**Show the architecture diagram and say:**

```
Employee  →  Chat UI (React)  →  Chatbot API (Claude)  →  Payroll API  →  MongoDB
   ↑              ↑                      ↑
   └── Natural language ──── Tool calls ──── Real payroll data
```

> "Here's how it works end to end:"

1. **Payroll API** — ASP.NET Core REST API backed by MongoDB. Manages employees, time entries, tax info, and deductions. Publishes domain events to Kafka.
2. **Chatbot API** — A thin .NET service that connects Claude (Anthropic's AI) to our payroll data via tool calling. Claude can query 6 different endpoints to answer questions.
3. **Chat UI** — A React interface inside our Listener Client app. Employee selects their name, types a question, gets a markdown-formatted response.
4. **Event Pipeline** — Kafka + Dapr + a GraphQL Listener API with real-time subscriptions, so downstream systems stay in sync.

> "The key insight: Claude doesn't just retrieve data — it *calculates* the EWA balance by combining employee info, hours worked, tax withholdings, and deductions in real time."

---

## SLIDE 4: The EWA Balance Engine (1.5 minutes)

**Say:**

> "Let me walk through the withdrawal logic — this is the business rules engine we built."

### Calculation Pipeline:

| Step | What Happens |
|------|-------------|
| 1. Gross Earned Wages | Hourly: hours worked x rate. Salary: annual / 26 pay periods, prorated by days elapsed |
| 2. Estimated Taxes | Federal (22% base), State (5% or 0% for FL, TX, WA, etc.), FICA (7.65%), adjusted by allowances |
| 3. Deductions | Health insurance ($250), 401k (6%), dental, vision — fixed or percentage-based |
| 4. Net Earned | Gross minus taxes minus deductions — this is the employee's balance |
| 5. Available Withdrawal | **The lesser of $200 or 70% of net earned** — max 1 per day |

### Examples:

> "Here's how it plays out for different employees:"

| Employee | Net Earned | 70% of Net | Daily Cap | Available Today |
|----------|-----------|-----------|-----------|-----------------|
| John (Salary $75k) | ~$1,800 | $1,260 | $200 | **$200** (capped) |
| Sarah (Hourly $28.50, 20hrs) | ~$380 | $266 | $200 | **$200** (capped) |
| New hire, 2 days in | ~$100 | $70 | $200 | **$70** (70% rule) |

> "The system always explains *why* the limit is what it is — the employee sees both their full balance and the withdrawal cap."

---

## SLIDE 5: Live Demo — Chat UI (3-4 minutes)

**Open http://localhost:3001 in browser, click the "EWA Chat" tab.**

### Demo Script:

**Step 1: Show the interface**

> "Here's our chat interface. It lives as a tab alongside the existing Change Stream and Employee Records views — no separate app needed."

- Point out the employee selector dropdown
- Point out the quick-action chips
- Point out the clean, familiar UI matching the existing design system

**Step 2: Select "John" and ask a basic question**

Type: `What data do you have on me?`

> "First, let's see what the system knows about John. Claude will look up his employee record, tax info, deductions, and time entries — all in one response."

*Wait for response. Point out the markdown formatting — headers, bullet points, currency formatting.*

**Step 3: Ask about the EWA balance**

Type: `How much can I withdraw today?`

> "Now the money question. Claude calls our EWA balance tool, which fetches John's data, calculates gross earned wages for this pay period, subtracts estimated taxes and deductions, and applies our withdrawal rules."

*Wait for response. Highlight:*
- Gross earned wages
- Tax and deduction breakdown
- Net earned balance vs. available withdrawal
- The explanation of why the limit is $200 (or 70%)

**Step 4: Ask a follow-up**

Type: `Why can't I withdraw more?`

> "This is where conversational AI shines. The employee can ask follow-ups and Claude explains the business rules in plain English — no support ticket needed."

**Step 5: (Optional) Switch to a different employee**

Select "Sarah" (hourly), type: `What's my balance?`

> "Sarah is hourly, so her balance is based on actual hours worked times her rate. Different calculation, same interface."

---

## SLIDE 6: What Claude Can Do (1 minute)

**Say:**

> "The chatbot has 6 tools available — all read-only:"

| Tool | What It Does |
|------|-------------|
| `get_all_employees` | Find employees by name |
| `get_employee_by_id` | Full employee profile |
| `get_time_entries` | Clock in/out records |
| `get_tax_information` | Federal & state withholding |
| `get_deductions` | Health, 401k, dental, etc. |
| `get_ewa_balance` | **Full balance + withdrawal calculation** |

> "Claude decides which tools to call based on the question. It can chain multiple calls — for example, it first looks up the employee by name, then fetches their balance by ID. Up to 10 tool round-trips per conversation."

> "And it's explicitly read-only — if you ask it to change your tax withholdings, it politely declines."

---

## SLIDE 7: Tech Stack (30 seconds)

**Quick flash of the stack:**

| Layer | Technology |
|-------|-----------|
| AI | Claude Sonnet 4.5 via Anthropic SDK |
| Chat Backend | ASP.NET Core 7 + Tool Calling |
| Payroll API | ASP.NET Core + MediatR + Orleans |
| Databases | MongoDB (write) + MySQL (read) |
| Messaging | Kafka + Dapr |
| Frontend | React 19 + Vite + react-markdown |
| Infrastructure | Docker Compose (12 services) |

> "Everything runs locally in Docker. One `docker-compose up` and you have the full platform."

---

## SLIDE 8: What's Next — V2 Roadmap (30 seconds)

**Say:**

> "For V2, we'd add:"

- **Real authentication** — SSO instead of a name dropdown
- **Withdrawal execution** — actually process the transfer, not just calculate
- **Push notifications** — 'Your balance just increased' via GraphQL subscriptions
- **Conversation persistence** — save chat history across sessions
- **Multi-language support** — Claude handles this natively
- **Audit logging** — every balance inquiry logged for compliance

---

## SLIDE 9: Key Takeaways (30 seconds)

**Say:**

> "Three things to take away:"

1. **AI + payroll data = self-service support.** Employees get instant answers instead of filing tickets.
2. **Tool calling is the pattern.** Claude doesn't hallucinate numbers — it calls real APIs and calculates from real data.
3. **We built this in a hackathon.** Chat UI, balance engine, withdrawal rules, full Docker deployment — all wired into the existing event-driven architecture.

> "Questions?"

---

## APPENDIX: Quick Reference

### Service URLs (for live demo)
- Chat UI: http://localhost:3001 (EWA Chat tab)
- Payroll Frontend: http://localhost:3000
- Payroll API Swagger: http://localhost:5000/swagger
- GraphQL Playground: http://localhost:5001/graphql
- Kafka UI: http://localhost:8080
- Zipkin Tracing: http://localhost:9411

### Seed Employees
| Name | Pay Type | Rate |
|------|----------|------|
| John Smith | Salary | $75,000/yr |
| Sarah Johnson | Hourly | $28.50/hr |
| Michael Williams | Salary | $85,000/yr |
| Emily Brown | Hourly | $32.00/hr |
| David Davis | Salary | $95,000/yr |

### Files We Built/Modified
**New files:**
- `listenerClient/src/components/ChatView.jsx` — Main chat container
- `listenerClient/src/components/ChatMessage.jsx` — Message bubbles with markdown
- `listenerClient/src/components/ChatIdentityBar.jsx` — Employee selector
- `listenerClient/src/api/chat.js` — Chat API client
- `src/ChatbotApi/Services/EwaBalanceCalculator.cs` — Balance + withdrawal engine

**Modified files:**
- `listenerClient/src/App.jsx` — Added EWA Chat tab
- `listenerClient/src/index.css` — Chat UI styles
- `listenerClient/vite.config.js` — Chat API proxy
- `listenerClient/nginx.conf` — Production proxy to chatbot-api
- `listenerClient/package.json` — Added react-markdown, remark-gfm
- `src/ChatbotApi/Services/ChatService.cs` — Updated system prompt with EWA rules
- `src/ChatbotApi/Tools/ToolDefinitions.cs` — Added get_ewa_balance tool
- `src/ChatbotApi/Tools/ToolExecutor.cs` — Wired up EWA balance calculator
- `src/ChatbotApi/Program.cs` — Registered EwaBalanceCalculator in DI
- `docker-compose.yaml` — Added env_file for API key
