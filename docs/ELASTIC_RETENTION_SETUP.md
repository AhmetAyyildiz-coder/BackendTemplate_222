# Elasticsearch retention (90 days) for Serilog logs

This repo can send API logs to Elasticsearch (via `Elastic.Serilog.Sinks`). Retention is managed on the Elasticsearch side, not in C#.

## 1) Start Elasticsearch + Kibana

From repo root:

```bash
docker-compose up -d
```

- Elasticsearch: http://localhost:9200
- Kibana: http://localhost:5601

## 2) Enable log shipping from the API

In `BackendTemplate.Api/appsettings.json`:

- Set `ElasticLogging.Enabled` to `true`
- Ensure `ElasticLogging.Nodes` contains `http://localhost:9200`

Then run the API and hit an endpoint. The sink will write into the `logs-dotnet-default` data stream by default.

## 3) Create a 90-day retention policy (ILM)

### Option A — Kibana UI

1. Open Kibana: http://localhost:5601
2. Go to **Stack Management** → **Index Lifecycle Policies**
3. Create a policy, e.g. `logs-90d-delete`
4. Add a **Delete** phase with **Minimum age** = `90d`

Then attach it using an index template for the data stream:

1. **Stack Management** → **Index Management** → **Index Templates**
2. Create a composable template, e.g. `logs-dotnet-default-template`
3. Index patterns: `logs-dotnet-default*`
4. Enable **Data stream**
5. In template settings set:

```json
{
  "index.lifecycle.name": "logs-90d-delete"
}
```

### Option B — Elasticsearch API (curl)

Create ILM policy:

```bash
curl -sS -X PUT http://localhost:9200/_ilm/policy/logs-90d-delete \
  -H 'Content-Type: application/json' \
  -d '{
    "policy": {
      "phases": {
        "hot": { "actions": {} },
        "delete": { "min_age": "90d", "actions": { "delete": {} } }
      }
    }
  }'
```

Create index template for the data stream:

```bash
curl -sS -X PUT http://localhost:9200/_index_template/logs-dotnet-default-template \
  -H 'Content-Type: application/json' \
  -d '{
    "index_patterns": ["logs-dotnet-default*"],
    "data_stream": {},
    "template": {
      "settings": {
        "index.lifecycle.name": "logs-90d-delete"
      }
    }
  }'
```

## Notes

- Retention (90 days) is a cluster policy; the app only ships logs.
- If you later move to Elastic Cloud or enable security, you will set credentials (API key or user/pass) in the sink configuration.
