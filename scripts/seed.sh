#!/bin/bash
set -e

# Minimal jq replacement using python3 (cp-kafka image has python3 but not jq)
jq() {
  local expr=""
  if [ "$1" = "-r" ]; then shift; fi
  expr="$1"
  python3 -c "
import json, sys
data = json.load(sys.stdin)
path = sys.argv[1]
if path.startswith('.[].'):
    field = path[4:]
    for item in data:
        print(item.get(field, ''))
elif path.startswith('.'):
    field = path[1:]
    print(data.get(field, ''))
" "$expr"
}

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

log "Installing database client packages..."
python3 -m pip install --quiet --break-system-packages pymongo mysql-connector-python 2>/dev/null \
  || python3 -m pip install --quiet pymongo mysql-connector-python 2>/dev/null \
  || fail "Could not install Python database packages (pymongo, mysql-connector-python)"

# 1a. Clear MongoDB
log "Clearing MongoDB (payroll_db)..."
python3 << 'PYEOF'
from pymongo import MongoClient
client = MongoClient('mongodb://mongodb:27017/?replicaSet=rs0&directConnection=true')
db = client['payroll_db']
collections = db.list_collection_names()
if collections:
    for col in collections:
        db.drop_collection(col)
        print(f"  Dropped collection: {col}")
else:
    print("  No collections to drop")
client.close()
PYEOF

# 1b. Clear MySQL
log "Clearing MySQL (listener_db)..."
python3 << 'PYEOF'
import mysql.connector
conn = mysql.connector.connect(
    host='mysql', database='listener_db',
    user='listener_user', password='listener_password'
)
cursor = conn.cursor()
try:
    cursor.execute('DELETE FROM EmployeeRecords')
    conn.commit()
    print(f"  Deleted {cursor.rowcount} rows from EmployeeRecords")
except mysql.connector.errors.ProgrammingError:
    print("  EmployeeRecords table does not exist yet, skipping")
cursor.close()
conn.close()
PYEOF

# 1c. Purge Kafka topic data
# IMPORTANT: We purge (not delete) topics so that Dapr sidecar Kafka producers
# keep their connections. Deleting topics breaks the outbox → Kafka pipeline
# until the sidecar is restarted.
log "Purging Kafka topics..."
BOOTSTRAP=kafka:9092

# Ensure all application topics exist (idempotent, same as kafka-init)
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic timeentry-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic taxinfo-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic deduction-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic payperiod-hours-changed
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-gross-pay
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-net-pay --config cleanup.policy=compact,delete
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-search --config cleanup.policy=compact
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-info --config cleanup.policy=compact

# Purge non-compacted topics via kafka-delete-records (3 partitions each)
PURGE_TOPICS="employee-events timeentry-events taxinfo-events deduction-events payperiod-hours-changed employee-gross-pay"
python3 -c "
import json
topics = '$PURGE_TOPICS'.split()
offsets = [{'topic': t, 'partition': p, 'offset': -1}
           for t in topics for p in range(3)]
json.dump({'partitions': offsets}, open('/tmp/offsets.json', 'w'))
"
kafka-delete-records --bootstrap-server $BOOTSTRAP --offset-json-file /tmp/offsets.json 2>/dev/null \
  || log "  (some partitions may be empty, skipping)"
log "  Purged non-compacted topics"

# Delete and recreate compacted topics (kafka-delete-records can't fully purge compacted topics).
# employee-info is included because elasticsearch-updater may auto-create it with 1 partition
# before seed runs; deleting ensures it gets recreated with the correct 3 partitions.
kafka-topics --delete --topic employee-net-pay --bootstrap-server $BOOTSTRAP 2>/dev/null || true
kafka-topics --delete --topic employee-search --bootstrap-server $BOOTSTRAP 2>/dev/null || true
kafka-topics --delete --topic employee-info --bootstrap-server $BOOTSTRAP 2>/dev/null || true
sleep 2
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-net-pay --config cleanup.policy=compact,delete
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-search --config cleanup.policy=compact
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-info --config cleanup.policy=compact
log "  Recreated compacted topics (employee-net-pay, employee-search, employee-info)"

# Fix partition count for any topics that were auto-created with 1 partition by consumers
# (e.g., elasticsearch-updater subscribes to employee-info and employee-net-pay at startup,
# triggering Kafka auto-creation before seed runs). --alter --partitions is idempotent when
# the topic already has the target partition count.
ALL_TOPICS="employee-events timeentry-events taxinfo-events deduction-events payperiod-hours-changed employee-gross-pay employee-net-pay employee-search employee-info"
for topic in $ALL_TOPICS; do
  kafka-topics --alter --topic $topic --partitions 3 --bootstrap-server $BOOTSTRAP 2>/dev/null || true
done
log "  Verified all topics have 3 partitions"

log "Clean slate complete."

# ── 1d. Elasticsearch index setup ────────────────────────────────────────

ES="http://elasticsearch:9200"

log "Waiting for Elasticsearch..."
until curl -sf "$ES/_cluster/health" > /dev/null 2>&1; do
  sleep 5
done
log "  Elasticsearch is ready."

# Delete existing index (clean slate)
curl -sf -X DELETE "$ES/employee-search" > /dev/null 2>&1 || true

# Create index with explicit mappings
log "Creating employee-search index with mappings..."
curl -sf -X PUT "$ES/employee-search" \
  -H 'Content-Type: application/json' \
  -d '{
  "settings": {
    "number_of_shards": 1,
    "number_of_replicas": 0
  },
  "mappings": {
    "properties": {
      "employee_id": { "type": "keyword" },
      "first_name": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
      "last_name": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
      "email": { "type": "keyword" },
      "pay_type": { "type": "keyword" },
      "pay_rate": { "type": "double" },
      "pay_period_hours": { "type": "double" },
      "is_active": { "type": "boolean" },
      "hire_date": { "type": "date", "format": "strict_date_optional_time||yyyy-MM-dd'\''T'\''HH:mm:ss'\''Z'\''||yyyy-MM-dd'\''T'\''HH:mm:ssX||epoch_millis" },
      "pay_periods": {
        "type": "nested",
        "properties": {
          "pay_period_number": { "type": "long" },
          "gross_pay": { "type": "double" },
          "federal_tax": { "type": "double" },
          "state_tax": { "type": "double" },
          "additional_federal_withholding": { "type": "double" },
          "additional_state_withholding": { "type": "double" },
          "total_tax": { "type": "double" },
          "total_fixed_deductions": { "type": "double" },
          "total_percent_deductions": { "type": "double" },
          "total_deductions": { "type": "double" },
          "net_pay": { "type": "double" },
          "pay_rate": { "type": "double" },
          "pay_type": { "type": "keyword" },
          "total_hours_worked": { "type": "double" },
          "pay_period_start": { "type": "date", "format": "strict_date_optional_time||yyyy-MM-dd'\''T'\''HH:mm:ss" },
          "pay_period_end": { "type": "date", "format": "strict_date_optional_time||yyyy-MM-dd'\''T'\''HH:mm:ss" }
        }
      }
    }
  }
}' > /dev/null && log "  Index created." || log "  Index creation failed."

# ── 1e. Kafka Connect sink connector setup ───────────────────────────────

CONNECT="http://kafka-connect:8083"

log "Waiting for Kafka Connect..."
until curl -sf "$CONNECT/connectors" > /dev/null 2>&1; do
  sleep 5
done
log "  Kafka Connect is ready."

# Delete existing connector (idempotent)
curl -sf -X DELETE "$CONNECT/connectors/elasticsearch-sink" > /dev/null 2>&1 || true

# Register ES Sink Connector
log "Registering Elasticsearch sink connector..."
curl -sf -X POST "$CONNECT/connectors" \
  -H 'Content-Type: application/json' \
  -d '{
  "name": "elasticsearch-sink",
  "config": {
    "connector.class": "io.confluent.connect.elasticsearch.ElasticsearchSinkConnector",
    "topics": "employee-search",
    "connection.url": "http://elasticsearch:9200",
    "type.name": "_doc",
    "key.ignore": false,
    "schema.ignore": true,
    "key.converter": "org.apache.kafka.connect.storage.StringConverter",
    "value.converter": "org.apache.kafka.connect.json.JsonConverter",
    "value.converter.schemas.enable": false,
    "behavior.on.null.values": "delete",
    "write.method": "upsert"
  }
}' > /dev/null && log "  Connector registered." || log "  Connector registration failed."

# ── 1f. Initialize ksqlDB streams and tables ─────────────────────────────

KSQL="http://ksqldb-server:8088"

log "Waiting for ksqlDB server..."
until curl -sf "$KSQL/info" > /dev/null 2>&1; do
  sleep 5
done
log "  ksqlDB is ready."

# Terminate all running queries so DROP statements can succeed
log "Terminating existing ksqlDB queries..."
QUERY_IDS=$(curl -sf "$KSQL/ksql" \
  -H 'Content-Type: application/vnd.ksql.v1+json' \
  -d '{"ksql": "SHOW QUERIES;"}' \
  | grep -o '"id":"[^"]*"' | sed 's/"id":"//;s/"//') || true
for qid in $QUERY_IDS; do
  log "  Terminating query $qid"
  curl -sf -X POST "$KSQL/ksql" \
    -H 'Content-Type: application/vnd.ksql.v1+json' \
    -d "{\"ksql\": \"TERMINATE ${qid};\"}" > /dev/null || true
  sleep 1
done

log "Submitting ksqlDB statements..."
while IFS= read -r stmt; do
  [ -z "$stmt" ] && continue
  log "  Executing: $(echo "$stmt" | head -c 80)..."
  curl -sf -X POST "$KSQL/ksql" \
    -H 'Content-Type: application/vnd.ksql.v1+json' \
    -d "{\"ksql\": \"${stmt}\", \"streamsProperties\": {\"auto.offset.reset\": \"earliest\"}}" > /dev/null \
    && log "    OK" \
    || log "    FAILED"
  sleep 2
done < <(
  # Collapse multi-line SQL into single-line statements split by semicolons
  sed 's/--.*$//' /statements.sql | tr '\n' ' ' | sed 's/;/;\n/g' | sed 's/^[[:space:]]*//' | grep -v '^$'
)
log "  ksqlDB initialization complete."

# Allow ksqlDB consumers to fully start before producing events
sleep 10

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

# ── 6. Verify Elasticsearch ──────────────────────────────────────────────

log "Waiting for Elasticsearch documents to appear..."
sleep 15

ES_COUNT=$(curl -sf "$ES/employee-search/_count" 2>/dev/null | python3 -c "import json,sys; print(json.load(sys.stdin).get('count',0))" 2>/dev/null || echo "0")
log "  Elasticsearch employee-search index: $ES_COUNT documents"

CONNECTOR_STATE=$(curl -sf "$CONNECT/connectors/elasticsearch-sink/status" 2>/dev/null \
  | python3 -c "import json,sys; d=json.load(sys.stdin); print(d.get('connector',{}).get('state','UNKNOWN'))" 2>/dev/null || echo "UNKNOWN")
log "  Kafka Connect elasticsearch-sink connector: $CONNECTOR_STATE"

# ── Done ────────────────────────────────────────────────────────────────────

log ""
log "Seed complete!"
log "  5 employees created"
log "  40 time entries created (20 each for Sarah Johnson & Emily Brown)"
log "  5 tax records created"
log "  7 deductions created"
log "  $ES_COUNT Elasticsearch documents"
log ""
log "Verify with:"
log "  curl http://localhost:5000/api/employees"
log "  curl http://localhost:5000/api/timeentries/employee/$EMP2_ID"
log "  curl http://localhost:9200/employee-search/_search?pretty"
log "  curl http://localhost:8083/connectors/elasticsearch-sink/status"
log "  Check Kafka UI at http://localhost:8080"
