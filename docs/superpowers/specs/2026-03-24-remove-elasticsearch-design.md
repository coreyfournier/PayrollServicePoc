# Remove Elasticsearch and Hide Search Behind Config

**Date:** 2026-03-24
**Goal:** Lower the POC's resource footprint by removing Elasticsearch from the default Docker stack and hiding the frontend search feature behind a build-time flag. Preserve all code for future re-enablement.

## Strategy

Move Elasticsearch-related Docker services behind a compose profile. Hide the frontend search UI behind an environment variable. Keep all source code intact.

## Docker Compose Changes

### Move to `elasticsearch` profile
- `elasticsearch` service — add `profiles: ["elasticsearch"]`
- `elasticsearch-updater` service — add `profiles: ["elasticsearch"]`

### kafka-connect Dockerfile
- Remove `confluentinc/kafka-connect-elasticsearch:14.1.0` plugin install
- Keep Debezium MySQL connector

### seed.sh
- Remove `employee-search` topic creation
- Remove ES index creation and mapping setup
- Remove ES sink connector registration
- Remove ES health/status output
- Remove `elasticsearch` from seed `depends_on`
- Keep `kafka-connect` dependency (needed for Debezium connector registration)

## Frontend Changes

### Feature flag
- `VITE_ENABLE_SEARCH` environment variable, defaults to `false`
- SearchPanel in EmployeeList conditionally rendered based on flag

### nginx.conf
- Remove `/es/` proxy location block (no ES to route to)

## What stays unchanged
- `kafka-connect` service remains (Debezium only)
- `ElasticsearchUpdater/` Java source code stays in repo
- `frontend/src/components/search/` stays in repo
- `frontend/src/api/search.js` stays in repo
- `frontend/src/utils/searchQueryBuilder.js` stays in repo
- All search tests stay in repo

## Re-enablement
Run `docker-compose --profile elasticsearch up -d` to bring back ES services. Set `VITE_ENABLE_SEARCH=true` and rebuild the frontend to restore search UI. Re-add the ES connector plugin to kafka-connect Dockerfile.
