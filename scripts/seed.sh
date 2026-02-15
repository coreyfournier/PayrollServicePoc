#!/bin/sh
set -e

apk add --no-cache jq > /dev/null 2>&1

API="http://payroll-api:80/api"
LISTENER="http://listener-api:80"

# ── helpers ──────────────────────────────────────────────────────────────────

log()  { echo "==> $*"; }
fail() { echo "FATAL: $*" >&2; exit 1; }

# POST/PUT/DELETE with basic error handling. Prints response body to stdout.
api_post() {
  url="$1"; shift
  resp=$(curl -sf -X POST "$url" -H 'Content-Type: application/json' "$@") \
    || fail "POST $url failed"
  echo "$resp"
}

api_put() {
  url="$1"; shift
  resp=$(curl -sf -X PUT "$url" -H 'Content-Type: application/json' "$@") \
    || fail "PUT $url failed"
  echo "$resp"
}

api_delete() {
  curl -sf -X DELETE "$1" -o /dev/null || fail "DELETE $1 failed"
}

# ── 1. Clean slate ──────────────────────────────────────────────────────────

log "Clearing existing data..."

# Delete all employees from the payroll API (deactivates them)
existing=$(curl -sf "$API/employees") || fail "GET employees failed"
ids=$(echo "$existing" | jq -r '.[].id')
for id in $ids; do
  log "  Deleting employee $id"
  api_delete "$API/employees/$id"
done

# Clear the ListenerApi MySQL read model
log "  Clearing ListenerApi read model..."
curl -sf -X POST "$LISTENER/graphql" \
  -H 'Content-Type: application/json' \
  -d '{"query":"mutation { deleteAllEmployees { deletedCount success message } }"}' \
  > /dev/null || log "  (ListenerApi cleanup returned non-zero — may be empty already)"

log "Clean slate complete."

# ── 2. Create employees ────────────────────────────────────────────────────

log "Creating employees..."

EMP1=$(api_post "$API/employees" -d '{
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@example.com",
  "payType": 2,
  "payRate": 75000,
  "hireDate": "2020-01-15T00:00:00Z",
  "payPeriodHours": 40
}')
EMP1_ID=$(echo "$EMP1" | jq -r '.id')
log "  Created John Smith (Salary) — $EMP1_ID"

EMP2=$(api_post "$API/employees" -d '{
  "firstName": "Sarah",
  "lastName": "Johnson",
  "email": "sarah.johnson@example.com",
  "payType": 1,
  "payRate": 28.50,
  "hireDate": "2021-03-20T00:00:00Z",
  "payPeriodHours": 40
}')
EMP2_ID=$(echo "$EMP2" | jq -r '.id')
log "  Created Sarah Johnson (Hourly) — $EMP2_ID"

EMP3=$(api_post "$API/employees" -d '{
  "firstName": "Michael",
  "lastName": "Williams",
  "email": "michael.williams@example.com",
  "payType": 2,
  "payRate": 85000,
  "hireDate": "2019-06-01T00:00:00Z",
  "payPeriodHours": 40
}')
EMP3_ID=$(echo "$EMP3" | jq -r '.id')
log "  Created Michael Williams (Salary) — $EMP3_ID"

EMP4=$(api_post "$API/employees" -d '{
  "firstName": "Emily",
  "lastName": "Brown",
  "email": "emily.brown@example.com",
  "payType": 1,
  "payRate": 32.00,
  "hireDate": "2022-09-10T00:00:00Z",
  "payPeriodHours": 40
}')
EMP4_ID=$(echo "$EMP4" | jq -r '.id')
log "  Created Emily Brown (Hourly) — $EMP4_ID"

EMP5=$(api_post "$API/employees" -d '{
  "firstName": "David",
  "lastName": "Davis",
  "email": "david.davis@example.com",
  "payType": 2,
  "payRate": 95000,
  "hireDate": "2018-11-05T00:00:00Z",
  "payPeriodHours": 32
}')
EMP5_ID=$(echo "$EMP5" | jq -r '.id')
log "  Created David Davis (Salary, 32h) — $EMP5_ID"

# Small pause to let Dapr outbox flush employee events before time entries
sleep 2

# ── 3. Create time entries for hourly employees ────────────────────────────

# 4 weeks of Mon-Fri work days (20 days total, spanning 2 pay periods)
#   Week 1: Jan 19-23  (pay period 55)
#   Week 2: Jan 26-30  (pay period 55)
#   Week 3: Feb  2-6   (pay period 56)
#   Week 4: Feb  9-13  (pay period 56)

WORK_DAYS="
2026-01-19 08:00 16:30
2026-01-20 08:15 17:00
2026-01-21 08:30 16:45
2026-01-22 08:00 17:15
2026-01-23 08:45 17:00
2026-01-26 08:00 16:30
2026-01-27 08:15 16:45
2026-01-28 08:30 17:00
2026-01-29 08:00 17:15
2026-01-30 08:45 17:30
2026-02-02 08:00 16:30
2026-02-03 08:15 17:00
2026-02-04 08:30 16:45
2026-02-05 08:00 17:15
2026-02-06 08:45 17:00
2026-02-09 08:00 16:30
2026-02-10 08:15 17:00
2026-02-11 08:30 16:45
2026-02-12 08:00 17:15
2026-02-13 08:45 17:30
"

create_time_entries() {
  emp_id="$1"
  emp_name="$2"

  log "Creating time entries for $emp_name ($emp_id)..."

  echo "$WORK_DAYS" | while IFS=' ' read -r day clock_in clock_out; do
    # skip blank lines
    [ -z "$day" ] && continue

    # 1. Clock in (creates an open time entry with current timestamp)
    entry=$(api_post "$API/timeentries/clock-in/$emp_id")
    entry_id=$(echo "$entry" | jq -r '.id')

    # 2. Clock out (closes the time entry)
    sleep 0.5
    api_post "$API/timeentries/clock-out/$emp_id" > /dev/null

    # 3. PUT to set historical clock-in / clock-out times
    sleep 0.5
    api_put "$API/timeentries/$entry_id" \
      -d "{\"clockIn\": \"${day}T${clock_in}:00Z\", \"clockOut\": \"${day}T${clock_out}:00Z\"}" \
      > /dev/null

    log "    $day  ${clock_in}-${clock_out}"
    sleep 0.3
  done
}

create_time_entries "$EMP2_ID" "Sarah Johnson"
create_time_entries "$EMP4_ID" "Emily Brown"

# Pause to let events propagate
sleep 2

# ── 4. Create tax information ──────────────────────────────────────────────

log "Creating tax information..."

api_post "$API/taxinformation" -d "{
  \"employeeId\": \"$EMP1_ID\",
  \"federalFilingStatus\": \"Married\",
  \"federalAllowances\": 3,
  \"additionalFederalWithholding\": 0,
  \"state\": \"CA\",
  \"stateFilingStatus\": \"Married\",
  \"stateAllowances\": 3,
  \"additionalStateWithholding\": 0
}" > /dev/null
log "  John Smith — Married, CA, 3 allowances"

api_post "$API/taxinformation" -d "{
  \"employeeId\": \"$EMP2_ID\",
  \"federalFilingStatus\": \"Single\",
  \"federalAllowances\": 1,
  \"additionalFederalWithholding\": 50,
  \"state\": \"NY\",
  \"stateFilingStatus\": \"Single\",
  \"stateAllowances\": 1,
  \"additionalStateWithholding\": 25
}" > /dev/null
log "  Sarah Johnson — Single, NY, extra withholding"

api_post "$API/taxinformation" -d "{
  \"employeeId\": \"$EMP3_ID\",
  \"federalFilingStatus\": \"Married\",
  \"federalAllowances\": 4,
  \"additionalFederalWithholding\": 0,
  \"state\": \"TX\",
  \"stateFilingStatus\": \"Married\",
  \"stateAllowances\": 4,
  \"additionalStateWithholding\": 0
}" > /dev/null
log "  Michael Williams — Married, TX, 4 allowances"

api_post "$API/taxinformation" -d "{
  \"employeeId\": \"$EMP4_ID\",
  \"federalFilingStatus\": \"Single\",
  \"federalAllowances\": 1,
  \"additionalFederalWithholding\": 0,
  \"state\": \"WA\",
  \"stateFilingStatus\": \"Single\",
  \"stateAllowances\": 1,
  \"additionalStateWithholding\": 0
}" > /dev/null
log "  Emily Brown — Single, WA, 1 allowance"

api_post "$API/taxinformation" -d "{
  \"employeeId\": \"$EMP5_ID\",
  \"federalFilingStatus\": \"HeadOfHousehold\",
  \"federalAllowances\": 2,
  \"additionalFederalWithholding\": 100,
  \"state\": \"IL\",
  \"stateFilingStatus\": \"Single\",
  \"stateAllowances\": 2,
  \"additionalStateWithholding\": 50
}" > /dev/null
log "  David Davis — Head of Household, IL, extra withholding"

# ── 5. Create deductions ───────────────────────────────────────────────────

log "Creating deductions..."

# John Smith — health + 401k
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP1_ID\",
  \"deductionType\": 1,
  \"description\": \"Health Insurance - PPO Plan\",
  \"amount\": 250.00,
  \"isPercentage\": false
}" > /dev/null
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP1_ID\",
  \"deductionType\": 4,
  \"description\": \"401k Contribution\",
  \"amount\": 6,
  \"isPercentage\": true
}" > /dev/null
log "  John Smith — Health (\$250), 401k (6%)"

# Sarah Johnson — health + dental
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP2_ID\",
  \"deductionType\": 1,
  \"description\": \"Health Insurance - HMO Plan\",
  \"amount\": 180.00,
  \"isPercentage\": false
}" > /dev/null
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP2_ID\",
  \"deductionType\": 2,
  \"description\": \"Dental Insurance\",
  \"amount\": 45.00,
  \"isPercentage\": false
}" > /dev/null
log "  Sarah Johnson — Health (\$180), Dental (\$45)"

# Michael Williams — health + vision + 401k
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP3_ID\",
  \"deductionType\": 1,
  \"description\": \"Health Insurance - PPO Plan\",
  \"amount\": 250.00,
  \"isPercentage\": false
}" > /dev/null
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP3_ID\",
  \"deductionType\": 3,
  \"description\": \"Vision Insurance\",
  \"amount\": 25.00,
  \"isPercentage\": false
}" > /dev/null
api_post "$API/deductions" -d "{
  \"employeeId\": \"$EMP3_ID\",
  \"deductionType\": 4,
  \"description\": \"401k Contribution\",
  \"amount\": 10,
  \"isPercentage\": true
}" > /dev/null
log "  Michael Williams — Health (\$250), Vision (\$25), 401k (10%)"

# ── Done ────────────────────────────────────────────────────────────────────

log ""
log "Seed complete!"
log "  5 employees created"
log "  40 time entries created (20 each for Sarah Johnson & Emily Brown)"
log "  5 tax records created"
log "  7 deductions created"
log ""
log "Verify with:"
log "  curl http://localhost:5000/api/employees"
log "  curl http://localhost:5000/api/timeentries/employee/$EMP2_ID"
log "  Check Kafka UI at http://localhost:8080"
