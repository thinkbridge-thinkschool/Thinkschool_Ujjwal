# KQL queries for Application Insights

`operation_Id` in these tables holds the same W3C trace id we return as the `X-Trace-Id`
response header (see `CorrelationIdMiddleware`). A customer quoting that header value
leads straight to their request with the first query below.

## Slowest 10 requests in the last hour

```kql
// Which requests were slowest in the last hour, worth investigating first?
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, duration, resultCode, operation_Id
```

## Everything for a single operation_Id (== X-Trace-Id), in order

```kql
// Given one operation_Id (a customer's X-Trace-Id), what's the full story of that
// request - every log line, every dependency call, and the request itself?
let targetOperationId = "00000000000000000000000000000000"; // paste the X-Trace-Id / operation_Id here
union requests, dependencies, traces
| where operation_Id == targetOperationId
| project timestamp, itemType, name, message, duration, resultCode, severityLevel
| order by timestamp asc
```

## Alert condition: average duration of POST /api/quotes over the last 5 minutes

```kql
// Should the "slow quote creation" alert be firing right now?
requests
| where timestamp > ago(5m)
| where name == "POST /api/quotes"
| summarize avgDuration = avg(duration)
```
