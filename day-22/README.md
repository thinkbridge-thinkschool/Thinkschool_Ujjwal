# Day 22 — Extending resilience: bulkhead, idempotency, and a second pipeline around Redis

All code lives in [`day-5/QuotesApi`](../day-5/QuotesApi) — this folder
holds only the write-up.

## What already existed (read before touching anything)

From `day-5/RESILIENCE.md` and `Configuration/ResilienceOptions.cs`,
confirmed by reading the actual source, not just the doc:

- **One pipeline**, around `IRandomQuoteClient` (`GET /api/quotes/random`
  → zenquotes.io), registered via `AddHttpClient` + `AddResilienceHandler`
  in `Extensions/RandomQuoteClientExtensions.cs`.
- **Three strategies**, composed outermost→innermost as
  `TotalTimeout(10s) → Retry(3 attempts, 200ms base, exponential+jitter) → CircuitBreaker(50% ratio, 30s sampling, 10 min throughput, 30s break)`.
- Retry and circuit-breaker events already logged (`OnRetry`, `OnOpened`,
  `OnClosed`) via Serilog.
- Everything bound from `ResilienceOptions`/`Resilience` config section -
  nothing hardcoded.
- `GET /api/quotes/random` catches `HttpRequestException`,
  `TimeoutRejectedException`, `BrokenCircuitException` and returns `503`.
- Existing unit tests build the *real* pipeline against a stub
  `DelegatingHandler` (no network), asserting genuine Polly behavior.

**Not rebuilt.** Everything above is unchanged in shape; Day 22 only adds
to it (a bulkhead) and adds a second, independent pipeline elsewhere.

## What Day 22 adds

### 1. Bulkhead on the existing zenquotes pipeline

`RandomQuoteClientExtensions.cs`: `AddConcurrencyLimiter` (from
`System.Threading.RateLimiting`, already a transitive dependency via
`Microsoft.Extensions.Http.Resilience` - no new package) added as the
new **outermost** layer, before the pre-existing `TotalTimeout`.
`BulkheadMaxConcurrency`/`BulkheadMaxQueue` (10/5 default) added to
`ResilienceOptions`. `GET /api/quotes/random` now also catches
`RateLimiterRejectedException` distinctly, logged as `LogWarning`
("...rejected by bulkhead...") separately from the existing
timeout/circuit-breaker `LogError` catch - satisfies "log bulkhead
rejections distinctly from timeouts" for this pipeline.

### 2. Idempotency - stated explicitly, not just true by accident

**zenquotes pipeline:** `RandomQuoteClient.GetRandomQuoteAsync` is the
*only* caller of the `HttpClient` this pipeline wraps, and it issues
exactly one call: `_httpClient.GetAsync("api/random", ct)`. There is no
POST/PUT/DELETE anywhere in this client - confirmed by reading the whole
file, not assumed. Retrying is safe here unconditionally, not "by
convention," because there's structurally nothing else this pipeline
could ever be asked to retry.

**Redis pipeline (below):** `IDistributedCache` exposes exactly four
operations - Get, Set, Refresh, Remove - and all four are idempotent by
the nature of a key-value cache: re-setting a key to the same value or
deleting an already-gone key changes nothing. Retry is applied uniformly
across all four for this reason, documented directly on
`ResilientDistributedCache`.

### 3. Second pipeline: around Redis (the proof pipeline)

zenquotes.io can't be failed and restored on demand (it's a real public
API, not a private container this can stop/start) - it can prove retry
and "opens under load," but never recovery. Redis (`redis-cache`, Day
21's `HybridCache` L2) can be stopped and started at will, so it's what
demonstrates the full state machine.

**New — `Caching/ResilientDistributedCache.cs`:** implements
`IDistributedCache`, wrapping the real Redis-backed `IDistributedCache`
and routing every operation through a Polly `ResiliencePipeline`.

**New — `Extensions/RedisResilienceExtensions.cs`:** builds that
pipeline. Composition, outermost → innermost:

```
bulkhead → circuit breaker → retry → timeout
```

**The opposite nesting of the zenquotes pipeline** (which wraps
`timeout(total) → retry → circuit breaker`), deliberately:

- **Circuit breaker outside retry**: once open, a call is rejected with
  `BrokenCircuitException` and *never enters* the retry/timeout
  sub-pipeline - no attempt, no delay. That's what "the breaker should
  make degradation fast rather than slow" requires. (The zenquotes
  pipeline gets a similar practical effect the other way around - retry's
  `ShouldHandle` just declines to retry `BrokenCircuitException` once it
  sees one - but one retry-tier code path still runs first there.)
- **Timeout is per-attempt (innermost), not total.** Redis sits on the
  hot path of every cacheable request; a single attempt blowing past
  budget is itself the signal worth reacting to immediately, and because
  the breaker sits outside retry, a struggling-but-alive Redis still
  can't make one cache operation block for `attempts × delay × total`
  the way a total timeout would risk - which is exactly the "slow
  dependency starves everything else" failure mode the bulkhead
  requirement exists to prevent.

**`InfrastructureExtensions.cs`** no longer registers `IDistributedCache`
via `AddStackExchangeRedisCache` directly - it constructs the real
`RedisCache` by hand and wraps it in `ResilientDistributedCache` before
registering *that* as `IDistributedCache`. `HybridCache`'s own
registration is untouched; it resolves `IDistributedCache` for L2 exactly
as before and has no idea it's now resilience-wrapped. Caching
behavior/keys/tags/TTLs are unchanged - only the transport underneath L2
changed.

Both pipelines' numbers live in the same `ResilienceOptions` /
`Resilience` config section, `Redis`-prefixed for the new ones, per "bind
everything from ResilienceOptions." The Redis numbers are deliberately
different from the zenquotes ones (10s sampling instead of 30s, 4 minimum
throughput instead of 10) - Redis calls are normally sub-millisecond, so
neither a production incident nor this demo should need to wait
30s-and-10-samples to see the breaker react.

### 4. A real bug found via the resilience work, unrelated to it directly

Adding the Redis pipeline meant every `POST`/`DELETE` write now calls
`cache.RemoveByTagAsync("quotes", ct)` through it. Running the existing
integration suite (not assumed safe) turned up 5 new failures -
`POST /api/quotes` returning `500` instead of `201`. Traced it:
`RemoveByTagAsync` does **not** get `HybridCache`'s own broad L2-failure
protection the way `GetOrCreateAsync` does (proven in
`day-21/README.md`, reconfirmed for `BrokenCircuitException` in this
task's own live verification below) - left unguarded, a struggling Redis
turned a successful quote creation into a client-visible failure, on the
write path this time instead of the read path Day 21 already handled.
Fixed with a `try/catch` around both `RemoveByTagAsync` calls
(`EndpointExtensions.cs`): the quote is already durably committed by that
point, so a failed invalidation only costs a stale list for up to its
60s `Expiration` - logged as a warning, never surfaced to the caller.

## Live verification - what was actually observed

### Build and existing suite

```
dotnet build                          -> 0 errors, 4 pre-existing warnings (unrelated)
Quotes.Tests.Unit                     -> 68 passed, 0 failed
Quotes.Tests.Integration              -> 39 passed, 1 failed
```
The one failure is `SeedBehaviorTests.Startup_InProductionEnvironment_DoesNotSeedTestUser`
- pre-existing since Day 19 (`ServiceBusOptions` validation against a
test factory that deliberately forces `Production`, where user-secrets
don't load), documented there and unchanged by this task. **108 total,
same as the brief's baseline.**

*(Mid-task, before the `RemoveByTagAsync` fix above, this same suite
briefly showed 5 new failures - included here because "build clean,
report the count" should mean the real number observed, and the honest
number at one point in this task's history wasn't 39/40. Fixed, reverted
to baseline, confirmed by rerunning.)*

### A real finding about Polly's timeout, from actually testing it

The first attempt at driving the breaker showed each failing call taking
**~18 seconds** - wildly more than the configured 1s
`RedisPerAttemptTimeout`. Investigated rather than adjusted the test:
confirmed via a deliberate experiment (Redis client's own
`ConnectTimeout`/`SyncTimeout`/`AsyncTimeout` loosened to 10s, Polly's
timeout left at 1s) that a call against a stopped Redis took **33
seconds** end to end, with the outcome logged as `RedisConnectionException`
every time - **never once** `TimeoutRejectedException`. Polly v8's
`AddTimeout` is cooperative-only; it has no mechanism to forcibly abort a
`StackExchange.Redis` call stuck waiting in its internal backlog for a
connection that isn't respecting the token it was handed. The real fix -
now in `InfrastructureExtensions.cs` - is pinning the Redis client's own
`ConnectTimeout`/`SyncTimeout`/`AsyncTimeout` to `RedisPerAttemptTimeout`
directly, so the underlying client fails fast at the transport level.
Full reasoning left in `RedisResilienceExtensions.cs`'s own comments.
Polly's `AddTimeout` stays in the pipeline as a real backstop for a
different failure mode (one that does honor cancellation) - just not the
one this task's live verification exercised.

### THE FULL LIFECYCLE - the deliverable

Driven via a dev-only diagnostic endpoint
(`POST /api/diagnostics/redis-probe`, `Program.cs`) that calls
`IDistributedCache.SetAsync` directly with a fresh random key every
time - bypassing `HybridCache`/L1 entirely, since L1's 10s local cache
would otherwise mask most `GET /api/quotes` traffic from ever reaching
Redis at all. `docker stop redis-cache`, six genuinely concurrent probes
fired at once (so enough failures land inside the 10s sampling window -
sequential calls at ~5s each would age out of the window before
`MinimumThroughput` was reached, also discovered by testing, not
predicted), then `docker start redis-cache` partway through and kept
driving until recovery. Every line below is pasted verbatim from the
running app's log:

```
15:16:20 [ERR] Redis circuit breaker CLOSED -> OPEN for 00:00:15 after outcome RedisConnectionException
15:16:44 [WRN] Redis circuit breaker OPEN -> HALF-OPEN (allowing one probe call through)
15:16:49 [ERR] Redis circuit breaker CLOSED -> OPEN for 00:00:15 after outcome RedisConnectionException
15:17:13 [WRN] Redis circuit breaker OPEN -> HALF-OPEN (allowing one probe call through)
15:17:19 [ERR] Redis circuit breaker CLOSED -> OPEN for 00:00:15 after outcome RedisConnectionException
15:17:52 [WRN] Redis circuit breaker OPEN -> HALF-OPEN (allowing one probe call through)
15:17:52 [INF] Redis circuit breaker HALF-OPEN -> CLOSED (probe succeeded)
```

Every one of `Closed→Open`, `Open→HalfOpen`, and `HalfOpen→Closed` is
present, with timestamps - and, not manufactured but genuinely what
happened: the breaker tried to recover **twice** (15:16:44 and 15:17:13)
while Redis was still actually down, correctly found the probe still
failing both times, and went straight back to `Open` rather than closing
prematurely. `docker start redis-cache` landed at 15:17:40, in between
the second and third `Open` periods; the very next half-open probe
(15:17:52) found a live Redis and closed. This is the honest sequence a
real recovery produces, not a cleaned-up single-attempt version of it.

### Fast rejection vs. closed-and-failing - the latency claim, measured

| State | Call | Elapsed | Outcome |
|---|---|---|---|
| Closed, failing (real retry sequence against dead Redis) | probe | ~5-6s | `RedisConnectionException` |
| Half-open (one real probe attempt) | probe | ~5-6s | `RedisConnectionException` |
| **Open** (breaker rejects before any attempt) | probe | **0s** | `BrokenCircuitException` |

Captured directly after a fresh `Closed→Open` transition (breaker known
still open): a probe call returned in **0 seconds** with
`BrokenCircuitException`, versus ~5-6 seconds for the real
retry-wrapped attempts that preceded it. This is the concrete difference
between "the breaker doesn't exist" (every failing request pays the full
retry cost) and "the breaker exists" (a known-bad dependency is rejected
before any network call is even attempted).

### `GET /api/quotes` stays up throughout

With the breaker in its `Open` state from the sequence above:

```
GET /api/quotes?page=1&size=5  -> 200, elapsed 0s
```

The app log shows exactly what happens underneath - `HybridCache`'s own
broad L2-failure handling (first proven in `day-21/README.md` for
`RedisConnectionException`, now reconfirmed for a *different* exception
type entirely) catches the `BrokenCircuitException` the same way:

```
[ERR] Cache backend read failure.
Polly.CircuitBreaker.BrokenCircuitException: The circuit is now open and is not allowing calls.
 ---> StackExchange.Redis.RedisConnectionException: ...
```

Logged and swallowed inside `HybridCache`, never reaching the endpoint -
the request falls through to the database and returns normally. Day 21
already showed this degradation happens; this task's job was making it
*fast* once the breaker trips, which the table above demonstrates.

### The write path, with Redis down (post-fix)

```
POST /api/quotes (Redis stopped)  -> 201 Created, elapsed ~6s
[WRN] Failed to invalidate cached quotes list after creating quote 42 - list may serve stale data until it expires.
```

The quote is created successfully; the cache invalidation attempt still
runs its real retry sequence (hence ~6s, matching the closed-and-failing
number above) but its failure is caught, logged, and never turned into a
client-visible error. Before the fix documented above, this same call
returned `500`.

### Bulkhead - rejections logged distinctly, not conflated with timeouts

Redis reachable (so the bulkhead, not the breaker, is what's under test):
45 genuinely concurrent probes fired against `PermitLimit=20` /
`QueueLimit=10` (30 total capacity):

```
ok=30 rejected=15 other=0
```

```
[WRN] Redis SetAsync rejected by bulkhead - too many concurrent calls in flight.
```
(×15, one per rejected call, each carrying its own request's TraceId).
Every rejection came back as `RateLimiterRejectedException` specifically
- a distinct type from `TimeoutRejectedException`, and logged with a
distinct message ("...rejected by bulkhead...") from the timeout branch
in the same `ResilientDistributedCache.ExecuteSync`/`ExecuteAsync` helper
methods, so the two are never conflated in the log even though both would
look like "the call didn't work" from outside.

## What could not be verified

- **A genuine `TimeoutRejectedException` in normal operation** wasn't
  observed - explained above: Redis's own configured timeout and Polly's
  are set to the same boundary, and the underlying client's failure
  dominates. The isolation experiment proves the two are independent
  mechanisms and that Polly's alone doesn't work here, but doesn't
  produce a "here's a real `TimeoutRejectedException` from ordinary
  traffic" log line, because ordinary traffic never hits that path in
  this configuration.
- **A true concurrent race on the circuit breaker's own state** (two
  requests simultaneously deciding whether a half-open probe should be
  the one that runs) wasn't specifically isolated - Polly's breaker
  implementation is assumed thread-safe (it's the library's job, not this
  code's), not independently verified here.
- **Multi-instance bulkhead behavior** - `ConcurrencyLimiterOptions` is
  per-process (in-memory), same limitation Day 20's outbox discussion
  already named for a different mechanism: this app runs as one instance
  today, so a shared, cross-instance concurrency limit was never a
  requirement here, but is worth naming as something this design doesn't
  provide.
- **The zenquotes pipeline's bulkhead**, specifically tripping it live
  against the real zenquotes.io - the Redis pipeline's bulkhead was
  proven instead (control over start/stop, and 30+ genuinely concurrent
  calls are far easier to drive safely against a local container than
  against someone else's public API). The zenquotes bulkhead is
  structurally the same code path (`AddConcurrencyLimiter`, same
  `RateLimiterRejectedException` handling now added to that endpoint
  too), just not independently load-tested in this task.
