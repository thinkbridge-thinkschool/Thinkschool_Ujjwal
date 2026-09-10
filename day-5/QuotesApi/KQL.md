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

## p50 and p99 duration by endpoint

```kql
// An average hides a bad tail. A good p50 with a bad p99 means most users are fine
// and some are having a genuinely terrible time - that's invisible in avg(duration).
requests
| where timestamp > ago(1h)
| summarize count = count(), p50 = percentile(duration, 50), p99 = percentile(duration, 99) by name
| order by p99 desc
```

## Dependency breakdown — where is request time actually going

Two views: aggregate (which dependency TYPE dominates overall) and per-request (how much of THIS request was our own code versus waiting on something else).

```kql
// Aggregate: SQL vs Redis vs outbound HTTP, across all requests in the window.
dependencies
| where timestamp > ago(1h)
| summarize totalDuration = sum(duration), avgDuration = avg(duration), count = count() by type, target
| order by totalDuration desc
```

```kql
// Per-request: of this request's total duration, how much was spent waiting on
// dependencies versus running our own code (selfDuration = requestDuration minus the
// sum of every dependency call's duration during that same operation_Id)?
requests
| where timestamp > ago(1h)
| project operation_Id, requestName = name, requestDuration = duration
| join kind=leftouter (
    dependencies
    | where timestamp > ago(1h)
    | summarize dependencyDuration = sum(duration), dependencyCount = count() by operation_Id
) on operation_Id
| extend dependencyDuration = coalesce(dependencyDuration, 0.0), dependencyCount = coalesce(dependencyCount, 0)
| extend selfDuration = requestDuration - dependencyDuration
| project requestName, requestDuration, dependencyDuration, selfDuration, dependencyCount
| order by requestDuration desc
```

## Error-rate alert: >5% of requests failing (5xx) in a 5-minute window, minimum 10 requests

```kql
// Should the error-rate alert be firing right now? Scoped to 5xx specifically, not
// success == false generally - a 401 from a bad login attempt or a 400 from a
// validation failure is normal client behavior, not a service health signal, and
// blending them in would make this fire on routine traffic. The >= 10 guard exists
// because 1 failure out of 2 total requests is 50% and statistically meaningless -
// this alert should not be able to fire on a quiet API with almost no traffic.
requests
| where timestamp > ago(5m)
| summarize total = count(), serverErrors = countif(toint(resultCode) >= 500)
| where total >= 10
| extend errorRatePercent = todouble(serverErrors) / total * 100
| where errorRatePercent > 5
```

## Requests issuing an abnormal number of dependency (DB) calls — N+1 finder

```kql
// Detects the N+1 signature: one request, many near-identical dependency calls sharing
// its operation_Id. Groups dependencies by operation_Id, counts them, and surfaces any
// request that made more than 10 database calls to reach its result.
dependencies
| where timestamp > ago(1h)
| where type in ("SQL", "SQLite")
| summarize dependencyCount = count(), sampleTarget = any(target) by operation_Id
| where dependencyCount > 10
| join kind=inner (
    requests
    | project operation_Id, name, requestDuration = duration, resultCode
) on operation_Id
| project operation_Id, name, requestDuration, dependencyCount, sampleTarget, resultCode
| order by dependencyCount desc
```
