# Remove Elasticsearch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Elasticsearch from the default Docker stack to lower the POC's resource footprint, preserving all code behind profiles and feature flags for re-enablement.

**Architecture:** Move `elasticsearch` and `elasticsearch-updater` Docker services behind a compose profile. Remove ES connector plugin from kafka-connect. Strip ES-related setup from the seed script. Hide the frontend search UI behind a `VITE_ENABLE_SEARCH` env var.

**Tech Stack:** Docker Compose profiles, Vite environment variables, React conditional rendering, bash

**Spec:** `docs/superpowers/specs/2026-03-24-remove-elasticsearch-design.md`

---

### Task 1: Move Docker services behind `elasticsearch` profile

**Files:**
- Modify: `docker-compose.yaml` (elasticsearch service ~line 283, elasticsearch-updater service ~line 353)

- [ ] **Step 1: Add profile to elasticsearch service**

Add `profiles: ["elasticsearch"]` to the `elasticsearch` service block (after the `healthcheck` block, before the next service):

```yaml
  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.12.0
    ...
    profiles: ["elasticsearch"]
    healthcheck:
      ...
```

- [ ] **Step 2: Add profile to elasticsearch-updater service**

Add `profiles: ["elasticsearch"]` to the `elasticsearch-updater` service block:

```yaml
  elasticsearch-updater:
    ...
    profiles: ["elasticsearch"]
    depends_on:
      ...
```

- [ ] **Step 3: Remove elasticsearch from kafka-connect depends_on**

In the `kafka-connect` service, remove the `elasticsearch: condition: service_healthy` entry from `depends_on`. Keep `kafka` and `mysql` dependencies. The result should be:

```yaml
    depends_on:
      kafka:
        condition: service_healthy
      mysql:
        condition: service_healthy
```

- [ ] **Step 4: Remove elasticsearch from seed depends_on**

In the `seed` service, remove the `elasticsearch: condition: service_healthy` entry from `depends_on`. Keep all other dependencies (kafka, ksqldb-server, payroll-api, listener-api, transfer-api, kafka-connect).

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yaml
git commit -m "chore: move elasticsearch services behind docker-compose profile"
```

---

### Task 2: Remove ES connector plugin from kafka-connect Dockerfile

**Files:**
- Modify: `docker/Dockerfile.kafka-connect`

- [ ] **Step 1: Remove the ES connector install line**

Remove this line from `docker/Dockerfile.kafka-connect`:
```dockerfile
RUN confluent-hub install --no-prompt confluentinc/kafka-connect-elasticsearch:14.1.0
```

The file should become:
```dockerfile
FROM confluentinc/cp-kafka-connect:7.5.0
RUN confluent-hub install --no-prompt debezium/debezium-connector-mysql:2.4.2
```

- [ ] **Step 2: Commit**

```bash
git add docker/Dockerfile.kafka-connect
git commit -m "chore: remove ES connector plugin from kafka-connect image"
```

---

### Task 3: Remove ES-related setup from seed script

**Files:**
- Modify: `scripts/seed.sh`

- [ ] **Step 1: Remove employee-search topic creation (line 196)**

Remove this line from the initial topic creation block (~line 196):
```bash
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-search --config cleanup.policy=compact
```

- [ ] **Step 2: Remove employee-search from compacted topic delete/recreate (lines 218, 222)**

Remove these two lines from the compacted topic cleanup section:
```bash
kafka-topics --delete --topic employee-search --bootstrap-server $BOOTSTRAP 2>/dev/null || true
```
and:
```bash
kafka-topics --create --if-not-exists --bootstrap-server $BOOTSTRAP --partitions 3 --replication-factor 1 --topic employee-search --config cleanup.policy=compact
```

Update the log message on the line after to:
```bash
log "  Recreated compacted topics (employee-net-pay, employee-info)"
```

- [ ] **Step 3: Remove employee-search from ALL_TOPICS (line 227)**

Change:
```bash
ALL_TOPICS="employee-events timeentry-events taxinfo-events deduction-events employee-net-pay employee-search employee-info transfer-requests transfer-events"
```
To:
```bash
ALL_TOPICS="employee-events timeentry-events taxinfo-events deduction-events employee-net-pay employee-info transfer-requests transfer-events"
```

- [ ] **Step 4: Remove entire Elasticsearch index section (lines 260-314)**

Remove the block from `# ── Elasticsearch index ──` through the index creation curl (ending at `|| log "  Index creation failed."`). This is lines 260-314.

- [ ] **Step 5: Remove ES sink connector registration (lines 324-346)**

Remove the `elasticsearch-sink` connector delete and registration block:
```bash
# Delete existing connector (idempotent)
curl -sf -X DELETE "$CONNECT/connectors/elasticsearch-sink" > /dev/null 2>&1 || true

# Register ES Sink Connector
log "Registering Elasticsearch sink connector..."
curl -sf -X POST "$CONNECT/connectors" \
  ...
}' > /dev/null && log "  Connector registered." || log "  Connector registration failed."
```

Keep the Debezium connector registration that follows.

- [ ] **Step 6: Remove ES verification in Phase 6 (lines 743-751)**

Remove these lines from the verification phase:
```bash
log "Waiting for Elasticsearch documents to appear..."
sleep 15

ES_COUNT=$(curl -sf "$ES/employee-search/_count" ...)
log "  Elasticsearch employee-search index: $ES_COUNT documents"

CONNECTOR_STATE=$(curl -sf "$CONNECT/connectors/elasticsearch-sink/status" ...)
log "  Kafka Connect elasticsearch-sink connector: $CONNECTOR_STATE"
```

- [ ] **Step 7: Remove ES references from seed summary (lines 763, 768-769)**

Remove the `$ES_COUNT Elasticsearch documents` summary line (~line 763).

Remove the two verification curl examples:
```bash
log "  curl http://localhost:9200/employee-search/_search?pretty"
log "  curl http://localhost:8083/connectors/elasticsearch-sink/status"
```

- [ ] **Step 8: Remove the ES variable definition (line 28)**

Remove:
```bash
ES="http://elasticsearch:9200"
```

- [ ] **Step 9: Commit**

```bash
git add scripts/seed.sh
git commit -m "chore: remove Elasticsearch setup from seed script"
```

---

### Task 4: Hide frontend search behind VITE_ENABLE_SEARCH flag

**Files:**
- Modify: `frontend/src/pages/EmployeeList.jsx`
- Modify: `frontend/nginx.conf`

- [ ] **Step 1: Add feature flag check and conditionally render SearchPanel**

In `frontend/src/pages/EmployeeList.jsx`, add a constant at the top of the file (after imports):

```javascript
const SEARCH_ENABLED = import.meta.env.VITE_ENABLE_SEARCH === 'true';
```

Wrap the SearchPanel rendering (line 183) in a conditional:

```jsx
{SEARCH_ENABLED && <SearchPanel onSearch={handleSearchResults} onReset={handleSearchReset} />}
```

Keep the SearchPanel import as-is (Vite tree-shakes unused code in production builds).

- [ ] **Step 2: Remove the /es/ proxy block from nginx.conf**

Remove lines 15-22 from `frontend/nginx.conf`:
```nginx
    # Proxy Elasticsearch requests
    location /es/ {
        set $upstream elasticsearch;
        rewrite ^/es/(.*) /$1 break;
        proxy_pass http://$upstream:9200;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
    }
```

- [ ] **Step 3: Run frontend lint to verify**

```bash
cd frontend && npm run lint
```

Expected: PASS (no errors)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/EmployeeList.jsx frontend/nginx.conf
git commit -m "feat: hide search UI behind VITE_ENABLE_SEARCH flag, remove ES proxy"
```

---

### Task 5: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update service ports table**

Remove the `elasticsearch | 9200 | Search index` row from the Service Ports table. The `kafka-connect` row stays (Debezium).

- [ ] **Step 2: Update Kafka Topics section**

Remove `employee-search` from the Kafka Topics list.

- [ ] **Step 3: Update Elasticsearch Updater section**

Add a note at the top of the "Elasticsearch Updater" section:

```
> **Profiled out by default.** Run `docker-compose --profile elasticsearch up -d` to enable. Requires re-adding the ES connector plugin to `docker/Dockerfile.kafka-connect`.
```

- [ ] **Step 4: Update Known Issues section**

Update the known issue about re-running ksqlDB to remove the `elasticsearch-updater` mention if it only applies when the profile is active.

- [ ] **Step 5: Add re-enablement instructions**

Add a section to CLAUDE.md (near the Docker commands section) explaining how to bring ES back:

```markdown
### Elasticsearch (optional, profiled out by default)
```bash
docker-compose --profile elasticsearch up -d  # Start ES services
```
Requires re-adding the ES connector plugin to `docker/Dockerfile.kafka-connect` and setting `VITE_ENABLE_SEARCH=true` for the frontend.
```

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for Elasticsearch removal"
```
