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
TRANSFER_API="http://transfer-api:80/api"
LISTENER="http://listener-api:80"
BOOTSTRAP=kafka:9092
KSQL="http://ksqldb-server:8088"
CONNECT="http://kafka-connect:8083"

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

# Execute a single ksqlDB statement. Returns 0 on success, 1 on failure.
ksql_exec() {
  local stmt="$1"
  log "  Executing: $(echo "$stmt" | head -c 80)..."
  curl -sf -X POST "$KSQL/ksql" \
    -H 'Content-Type: application/vnd.ksql.v1+json' \
    -d "{\"ksql\": \"${stmt}\", \"streamsProperties\": {\"auto.offset.reset\": \"earliest\"}}" > /dev/null \
    && log "    OK" \
    || { log "    FAILED"; return 1; }
}

# Parse statements.sql into individual statements
ksql_statements() {
  sed 's/--.*$//' /statements.sql | tr '\n' ' ' | sed 's/;/;\n/g' | sed 's/^[[:space:]]*//' | grep -v '^$'
}

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 1: Clear data stores (MongoDB, MySQL)
# ══════════════════════════════════════════════════════════════════════════════

log "Installing database client packages..."
python3 -m pip install --quiet --break-system-packages pymongo mysql-connector-python 2>/dev/null \
  || python3 -m pip install --quiet pymongo mysql-connector-python 2>/dev/null \
  || fail "Could not install Python database packages (pymongo, mysql-connector-python)"

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

log "Clearing MongoDB (transfer_db)..."
python3 << 'PYEOF'
from pymongo import MongoClient
client = MongoClient('mongodb://mongodb:27017/?replicaSet=rs0&directConnection=true')
db = client['transfer_db']
collections = db.list_collection_names()
if collections:
    for col in collections:
        db.drop_collection(col)
        print(f"  Dropped collection: {col}")
else:
    print("  No collections to drop")
client.close()
PYEOF

log "Clearing MySQL (listener_db)..."
python3 << 'PYEOF'
import mysql.connector

# Grant replication privileges for Debezium CDC (idempotent)
root_conn = mysql.connector.connect(
    host='mysql', user='root', password='root_password'
)
root_cursor = root_conn.cursor()
root_cursor.execute("GRANT REPLICATION SLAVE, REPLICATION CLIENT, RELOAD ON *.* TO 'listener_user'@'%'")
root_cursor.execute("GRANT SELECT, INSERT, UPDATE, DELETE ON listener_db.* TO 'listener_user'@'%'")
root_cursor.execute("FLUSH PRIVILEGES")
root_conn.commit()
root_cursor.close()
root_conn.close()
print("  Granted replication privileges to listener_user for Debezium CDC")

conn = mysql.connector.connect(
    host='mysql', database='listener_db',
    user='listener_user', password='listener_password'
)
cursor = conn.cursor()
for table in ['OutboxMessages', 'TransferRecords', 'BankAccounts', 'EmployeePayAttributes', 'EmployeeTransferStatuses', 'EmployeeRecords']:
    try:
        cursor.execute(f'DELETE FROM {table}')
        conn.commit()
        print(f"  Deleted {cursor.rowcount} rows from {table}")
    except mysql.connector.errors.ProgrammingError:
        print(f"  {table} table does not exist yet, skipping")
cursor.close()
conn.close()
PYEOF

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 2: Tear down ksqlDB (BEFORE touching Kafka topics)
#   ksqlDB objects hold references to topics. Deleting topics while ksqlDB
#   objects exist leaves ksqlDB in a broken state where DROPs and CREATEs fail.
# ══════════════════════════════════════════════════════════════════════════════

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

# Execute only DROP statements from statements.sql
# Use DELETE TOPIC on streams/tables that own their topic, but NOT on
# EMPLOYEE_EVENTS_RAW (external employee-events topic) or
# EMPLOYEE_NET_PAY_BY_PERIOD (SOURCE TABLE, doesn't own its topic).
log "Dropping existing ksqlDB objects..."
while IFS= read -r stmt; do
  [ -z "$stmt" ] && continue
  case "$stmt" in
    DROP*) ksql_exec "$stmt" || true ;;
  esac
  sleep 1
done < <(ksql_statements)
log "  ksqlDB teardown complete."

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 3: Purge and recreate Kafka topics
#   Safe now that ksqlDB has no references to these topics.
# ══════════════════════════════════════════════════════════════════════════════

log "Purging Kafka topics..."

# Ensure all application topics exist (idempotent)
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic timeentry-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic taxinfo-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic deduction-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-net-pay --config cleanup.policy=compact,delete
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-info --config cleanup.policy=compact
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic transfer-requests
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic transfer-events
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic transfer-limits --config cleanup.policy=compact

# Purge non-compacted topics via kafka-delete-records (3 partitions each)
PURGE_TOPICS="employee-events timeentry-events taxinfo-events deduction-events transfer-requests transfer-events"
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
kafka-topics --delete --topic employee-info --bootstrap-server $BOOTSTRAP 2>/dev/null || true
kafka-topics --delete --topic transfer-limits --bootstrap-server $BOOTSTRAP 2>/dev/null || true
sleep 2
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-net-pay --config cleanup.policy=compact,delete
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-info --config cleanup.policy=compact
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic transfer-limits --config cleanup.policy=compact
log "  Recreated compacted topics (employee-net-pay, employee-info, transfer-limits)"

# Fix partition count for any topics that were auto-created with 1 partition by consumers
ALL_TOPICS="employee-events timeentry-events taxinfo-events deduction-events employee-net-pay employee-info transfer-requests transfer-events transfer-limits"
for topic in $ALL_TOPICS; do
  kafka-topics --alter --topic $topic --partitions 3 --bootstrap-server $BOOTSTRAP 2>/dev/null || true
done
log "  Verified all topics have 3 partitions"

# Restart Kafka-dependent .NET services whose MassTransit Kafka Rider consumers
# fault when subscribed topics are deleted/recreated. The Java services
# (net-pay-processor, elasticsearch-updater) auto-recover, but MassTransit gets
# stuck in exponential backoff retries.
log "Restarting Kafka-dependent services..."
if [ -S /var/run/docker.sock ]; then
  for svc in listener-api transfer-api; do
    curl -sf -X POST "http://localhost/v1.24/containers/$svc/restart?t=5" \
      --unix-socket /var/run/docker.sock > /dev/null \
      && log "  Restarted $svc" \
      || log "  Could not restart $svc (may need manual restart)"
  done
  # Wait for both services to be healthy again
  until curl -sf "http://listener-api:80/graphql?query=%7B__typename%7D" > /dev/null 2>&1; do
    sleep 3
  done
  log "  listener-api is healthy."
  until curl -sf "http://transfer-api:80/api/transfers/employee/00000000-0000-0000-0000-000000000000" > /dev/null 2>&1; do
    sleep 3
  done
  log "  transfer-api is healthy."
else
  log "  Docker socket not available — skip service restart"
  log "  NOTE: You may need to manually restart listener-api and transfer-api if they have stale Kafka consumers"
fi

log "Clean slate complete."

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 4: Set up infrastructure (Kafka Connect, ksqlDB)
#   All backing topics exist and are empty. Create infrastructure on top.
# ══════════════════════════════════════════════════════════════════════════════

# ── Kafka Connect connectors ────────────────────────────────────────────

log "Waiting for Kafka Connect..."
until curl -sf "$CONNECT/connectors" > /dev/null 2>&1; do
  sleep 5
done
log "  Kafka Connect is ready."

# Register Debezium MySQL Source Connector (Outbox Pattern)
# Tails MySQL binlog, reads OutboxMessages table inserts, routes to Kafka topic
# specified in each row's Topic column. Uses AggregateId as Kafka message key.
log "Registering Debezium MySQL outbox connector..."
curl -sf -X DELETE "$CONNECT/connectors/debezium-outbox" > /dev/null 2>&1 || true
curl -sf -X POST "$CONNECT/connectors" \
  -H 'Content-Type: application/json' \
  -d '{
  "name": "debezium-outbox",
  "config": {
    "connector.class": "io.debezium.connector.mysql.MySqlConnector",
    "tasks.max": "1",
    "database.hostname": "mysql",
    "database.port": "3306",
    "database.user": "listener_user",
    "database.password": "listener_password",
    "database.server.id": "184054",
    "topic.prefix": "listener",
    "database.include.list": "listener_db",
    "table.include.list": "listener_db.OutboxMessages",
    "schema.history.internal.kafka.bootstrap.servers": "kafka:9092",
    "schema.history.internal.kafka.topic": "_debezium-schema-history",
    "transforms": "outbox",
    "transforms.outbox.type": "io.debezium.transforms.outbox.EventRouter",
    "transforms.outbox.table.field.event.id": "Id",
    "transforms.outbox.table.field.event.key": "AggregateId",
    "transforms.outbox.table.field.event.payload": "Payload",
    "transforms.outbox.table.field.event.timestamp": "CreatedAt",
    "transforms.outbox.route.by.field": "Topic",
    "transforms.outbox.route.topic.replacement": "${routedByValue}",
    "transforms.outbox.table.expand.json.payload": true,
    "key.converter": "org.apache.kafka.connect.storage.StringConverter",
    "value.converter": "org.apache.kafka.connect.json.JsonConverter",
    "value.converter.schemas.enable": false
  }
}' > /dev/null && log "  Debezium outbox connector registered." || log "  Debezium outbox connector registration failed."

# ── ksqlDB streams and tables ────────────────────────────────────────────
#   Topics are clean and ready. Create ksqlDB objects on top of them.

log "Creating ksqlDB streams and tables..."
while IFS= read -r stmt; do
  [ -z "$stmt" ] && continue
  case "$stmt" in
    CREATE*) ksql_exec "$stmt" || true ;;
  esac
  sleep 2
done < <(ksql_statements)
log "  ksqlDB initialization complete."

# Allow ksqlDB consumers to fully start before producing events
sleep 10

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 5: Seed data (parents first, then children)
#   Order: employees → bank accounts → time entries → tax info → deductions → transfers
# ══════════════════════════════════════════════════════════════════════════════

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

# Wait for all employees to be materialized in listener-api MySQL
# (employee-events must propagate through Kafka → listener-api consumer → MySQL)
log "Waiting for employees to appear in Listener API before creating bank accounts..."
EXPECTED_COUNT=5
until [ "$(curl -sf "$LISTENER/graphql" \
  -H 'Content-Type: application/json' \
  -d '{"query": "{ employees { id } }"}' 2>/dev/null | \
  python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d.get('data',{}).get('employees',[])))" 2>/dev/null)" -ge "$EXPECTED_COUNT" ] 2>/dev/null; do
  sleep 2
done
log "  All employees materialized in Listener API."

# ── Bank accounts ────────────────────────────────────────────────────────

log "Creating bank accounts..."

BA1=$(api_post "$LISTENER/api/bankaccounts" -d "{
  \"employeeId\": \"$EMP1_ID\",
  \"bankName\": \"Chase Bank\",
  \"accountNumberMasked\": \"1234\",
  \"routingNumber\": \"021000021\",
  \"accountType\": 1
}")
BA1_ID=$(echo "$BA1" | jq -r '.id')
log "  John Smith — Chase Bank ****1234 — $BA1_ID"

BA2=$(api_post "$LISTENER/api/bankaccounts" -d "{
  \"employeeId\": \"$EMP2_ID\",
  \"bankName\": \"Chase Bank\",
  \"accountNumberMasked\": \"5678\",
  \"routingNumber\": \"021000021\",
  \"accountType\": 1
}")
BA2_ID=$(echo "$BA2" | jq -r '.id')
log "  Sarah Johnson — Chase Bank ****5678 — $BA2_ID"

BA3=$(api_post "$LISTENER/api/bankaccounts" -d "{
  \"employeeId\": \"$EMP3_ID\",
  \"bankName\": \"Chase Bank\",
  \"accountNumberMasked\": \"9012\",
  \"routingNumber\": \"021000021\",
  \"accountType\": 1
}")
BA3_ID=$(echo "$BA3" | jq -r '.id')
log "  Michael Williams — Chase Bank ****9012 — $BA3_ID"

BA4=$(api_post "$LISTENER/api/bankaccounts" -d "{
  \"employeeId\": \"$EMP4_ID\",
  \"bankName\": \"Chase Bank\",
  \"accountNumberMasked\": \"3456\",
  \"routingNumber\": \"021000021\",
  \"accountType\": 1
}")
BA4_ID=$(echo "$BA4" | jq -r '.id')
log "  Emily Brown — Chase Bank ****3456 — $BA4_ID"

BA5=$(api_post "$LISTENER/api/bankaccounts" -d "{
  \"employeeId\": \"$EMP5_ID\",
  \"bankName\": \"Chase Bank\",
  \"accountNumberMasked\": \"7890\",
  \"routingNumber\": \"021000021\",
  \"accountType\": 1
}")
BA5_ID=$(echo "$BA5" | jq -r '.id')
log "  David Davis — Chase Bank ****7890 — $BA5_ID"

# ── Publish default transfer limits for each employee ────────────────────

log "Publishing default transfer limits..."
for EMP_ID in $EMP1_ID $EMP2_ID $EMP3_ID $EMP4_ID $EMP5_ID; do
  echo "${EMP_ID}:{\"EMPLOYEE_ID\":\"${EMP_ID}\",\"MAX_PER_PAY_PERIOD\":5,\"MAX_AMOUNT_PER_PAY_PERIOD\":10000.0,\"MAX_PER_DAY\":1}" | \
    kafka-console-producer --bootstrap-server $BOOTSTRAP --topic transfer-limits \
      --property "parse.key=true" --property "key.separator=:" 2>/dev/null
done
log "  Published default limits for 5 employees"

# ── Time entries (hourly employees only) ─────────────────────────────────

# Generate 20 most recent weekdays (Mon-Fri) relative to today, ensuring
# time entries always fall in the current and previous pay periods.
# Each day gets a varied clock-in/clock-out time for realism.
WORK_DAYS=$(python3 -c "
from datetime import datetime, timedelta, timezone
d = datetime.now(timezone.utc).date() - timedelta(days=1)  # start from yesterday
times = [('08:00','16:30'),('08:15','17:00'),('08:30','16:45'),('08:00','17:15'),('08:45','17:30')]
days = []
while len(days) < 20:
    if d.weekday() < 5:  # Mon-Fri
        cin, cout = times[len(days) % 5]
        days.append(f'{d.isoformat()} {cin} {cout}')
    d -= timedelta(days=1)
days.reverse()  # chronological order
print('\n'.join(days))
")
log "  Generated work days: $(echo "$WORK_DAYS" | head -1) through $(echo "$WORK_DAYS" | tail -1)"

create_time_entries() {
  emp_id="$1"
  emp_name="$2"

  log "Creating time entries for $emp_name ($emp_id)..."

  echo "$WORK_DAYS" | while IFS=' ' read -r day clock_in clock_out; do
    # skip blank lines
    [ -z "$day" ] && continue

    api_post "$API/timeentries" \
      -d "{\"employeeId\": \"${emp_id}\", \"clockIn\": \"${day}T${clock_in}:00Z\", \"clockOut\": \"${day}T${clock_out}:00Z\"}" \
      > /dev/null

    log "    $day  ${clock_in}-${clock_out}"
  done
}

create_time_entries "$EMP2_ID" "Sarah Johnson"
create_time_entries "$EMP4_ID" "Emily Brown"

# Pause to let events propagate
sleep 2

# ── Tax information ──────────────────────────────────────────────────────

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

# ── Deductions ───────────────────────────────────────────────────────────

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

# ── Transfers ────────────────────────────────────────────────────────────

log "Initiating transfers via Listener API..."

# Compute current pay period number (bi-weekly from 2024-01-01 epoch)
CURRENT_PAY_PERIOD=$(python3 -c "
from datetime import datetime, timezone
epoch_ms = 1704067200000  # 2024-01-01T00:00:00Z
period_ms = 14 * 24 * 60 * 60 * 1000  # 14 days
now_ms = int(datetime.now(timezone.utc).timestamp() * 1000)
print(int((now_ms - epoch_ms) // period_ms))
")
log "  Current pay period: $CURRENT_PAY_PERIOD"

# Wait for employees to be materialized in listener-api MySQL
# (employee-events must propagate through Kafka → listener-api consumer)
log "  Waiting for employees to appear in Listener API..."
until curl -sf "$LISTENER/api/Transfer/employee/$EMP2_ID/limits?payPeriodNumber=$CURRENT_PAY_PERIOD" > /dev/null 2>&1; do
  sleep 3
done
log "  Employees materialized in Listener API."

api_post "$LISTENER/api/Transfer" -d "{
  \"employeeId\": \"$EMP2_ID\",
  \"amount\": 100.00,
  \"payPeriodNumber\": $CURRENT_PAY_PERIOD,
  \"bankAccountId\": \"$BA2_ID\"
}" > /dev/null
log "  Sarah Johnson — \$100 transfer (period $CURRENT_PAY_PERIOD)"

api_post "$LISTENER/api/Transfer" -d "{
  \"employeeId\": \"$EMP4_ID\",
  \"amount\": 150.00,
  \"payPeriodNumber\": $CURRENT_PAY_PERIOD,
  \"bankAccountId\": \"$BA4_ID\"
}" > /dev/null
log "  Emily Brown — \$150 transfer (period $CURRENT_PAY_PERIOD)"

# ══════════════════════════════════════════════════════════════════════════════
# PHASE 6: Verify
# ══════════════════════════════════════════════════════════════════════════════

# ── Done ────────────────────────────────────────────────────────────────────

log ""
log "Seed complete!"
log "  5 employees created"
log "  5 bank accounts created"
log "  40 time entries created (20 each for Sarah Johnson & Emily Brown)"
log "  5 tax records created"
log "  7 deductions created"
log "  2 transfers initiated (via Listener API)"
log ""
log "Verify with:"
log "  curl http://localhost:5000/api/employees"
log "  curl http://localhost:5000/api/timeentries/employee/$EMP2_ID"
log "  Check Kafka UI at http://localhost:8080"
