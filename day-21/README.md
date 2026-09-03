# Day 21 — HybridCache (L1 + Redis L2) for GET /api/quotes

All app code lives in [`day-5/QuotesApi`](../day-5/QuotesApi) — this
folder holds only the write-up and the standalone load harness
(`load_test.py`).

## What changed

**New — `day-5/QuotesApi/Diagnostics/`:**
- `DbHitCounterInterceptor.cs` — an `EFCore` `DbCommandInterceptor`,
  registered as a singleton and handed to every `QuotesDbContext` via
  `AddInterceptors`, counting every SQL command the process actually
  sends. Day 11 (checked directly - its real source lives on the
  `day11-profile` branch, not in the `day-11/` folder on `main`) counted
  queries via OpenTelemetry spans exported to Application Insights and a
  KQL query over them, not a reusable in-process class - nothing existed
  to reuse, so this is new.
- `QuoteQueryCounter.cs` — a second, narrower counter, incrementing only
  inside `QuoteRepository.GetPagedAsync`. This exists because of a real
  problem found by actually running the load test, not anticipated in
  advance: `DbHitCounterInterceptor`'s count includes Day 20's
  `OutboxRelay`, which polls the `OutboxMessages` table every 5 seconds
  regardless of load-test traffic, on the same `QuotesDbContext`
  registration. A raw "total SQL commands" figure was noisy for exactly
  the "cold cache, N concurrent requests → 1 DB hit" claim this task
  needs to prove cleanly. `quotesQueries` is the number every measurement
  in this README is actually taken from.
- Both exposed via `GET /api/diagnostics/db-hits` and
  `POST /api/diagnostics/db-hits/reset` (`Program.cs`), gated behind
  `IsDevelopment()` so neither exists in a deployed build - this is
  measurement support, not product API.

**Edited — `Extensions/InfrastructureExtensions.cs`:**
- `AddStackExchangeRedisCache` (L2) + `AddHybridCache` (wraps it with an
  in-memory L1 automatically once both are registered).
- `ConnectionStrings:Redis` comes from `dotnet user-secrets` (set to
  `localhost:6379` for this local Redis container), never
  `appsettings.json` - same rule this repo already applies to `Jwt:Key`,
  `ServiceBus:ConnectionString`, etc.; this repo is public.

**Edited — `Extensions/EndpointExtensions.cs`:**
- `GET /api/quotes` now goes through `HybridCache.GetOrCreateAsync`,
  keyed on `quotes:page:{page}:size:{size}` and tagged `"quotes"`.
- `POST /api/quotes` and `DELETE /api/quotes/{id}` both call
  `cache.RemoveByTagAsync("quotes", ct)` after their write commits.
- A `Caching:Enabled` config check (default `true`) that bypasses
  `HybridCache` entirely when set `false` - exists purely so the
  "before" load-test baseline is a genuine uncached run, not a cache
  that's merely never been populated (which would still pay
  `HybridCache`'s own dispatch overhead). Not a production feature flag,
  just how the before/after comparison below was actually produced.

**Edited — `Data/QuoteRepository.cs`:** takes an optional
`QuoteQueryCounter` (see below for why optional), increments it in
`GetPagedAsync`.

## The cache key, and why it has to look like this

```
quotes:page:{page}:size:{size}
```

Both `page` and `size` are in the key because either one missing is a
real, silent bug: a key built from `page` alone would serve whatever
`size` happened to populate the cache first to every later caller
regardless of what they asked for, and vice versa. This isn't
hypothetical - it's exactly the kind of bug that passes a naive
smoke test ("GET /api/quotes works, returns 200, has quotes in it") while
silently returning wrong data. Verified live: `page=1&size=3` and
`page=2&size=3` return genuinely different quote ids, and Redis shows
four distinct keys (`page:1:size:3`, `page:1:size:5`, `page:1:size:10`,
`page:2:size:3`) after exercising a few different combinations - not one
key being reused.

## No user-specific data in this cache

`GET /api/quotes` is anonymous and its response carries nothing scoped to
the caller - not a "my quotes" filter, not anything keyed on
`createdByUserId`. That's what makes caching it by `page`/`size` alone
safe: every caller who asks for page 1 size 10 is supposed to see the
identical result. **The rule, stated explicitly because it will matter
the moment this stops being true:** a cache key that's missing a
dimension the response actually varies on means one caller can be served
another caller's data. If this endpoint ever grew a user-specific view,
the user's identity would have to become part of the key (or the
response would have to stop being cached at all) - there is no version
of "cache a personalized response by a key that ignores who's asking"
that doesn't leak.

## Expiration - two different numbers for two different jobs

```csharp
options.DefaultEntryOptions = new HybridCacheEntryOptions
{
    Expiration = TimeSpan.FromSeconds(60),          // L2 (Redis)
    LocalCacheExpiration = TimeSpan.FromSeconds(10), // L1 (in-memory)
};
```

**`Expiration` (60s, governs L2/Redis)** is a safety net, not the primary
consistency mechanism - `RemoveByTagAsync` on every write is what
actually keeps the list correct. This is deliberately generous because
its only job is bounding staleness in the case an invalidation is ever
missed, not making data visible sooner.

**`LocalCacheExpiration` (10s, governs L1/in-memory) is short for a
reason that has nothing to do with normal-case freshness.**
`RemoveByTagAsync` clears L2 (shared, in Redis) and the *calling*
instance's own L1 - but there is no cross-instance signal telling any
*other* instance's L1 to drop its copy. In a multi-instance deployment, a
short local TTL is the only thing bounding how long a different instance
can keep serving a stale L1 hit after someone else's write invalidated
the shared tag. This app runs as a single instance today (`day-17`'s F1
App Service), so that specific failure mode can't actually occur right
now - but the short TTL costs nothing, and it's the correct choice for
the *architecture*, not a number picked for today's instance count
specifically.

## Invalidation - tags, and what they cost

`RemoveByTagAsync` exists in this version (`Microsoft.Extensions.Caching.Hybrid`
10.1.0 - the NuGet listing's `10.0.11` doesn't actually exist for this
package, it versions by minor not patch on the 10.x train; resolved to
10.1.0, confirmed via `strings` on the installed DLL before writing any
code against it, then confirmed for real by building against it).
Without it, evicting "the whole list" after a write would require either
enumerating every `page`/`size` combination ever requested (the API has
no way to know what that set is) or falling back to a single unpaged
cache entry (defeating the point of paginating at all). The tag is
exactly the mechanism that makes "evict every page/size variant, without
knowing which ones exist" possible. **Cost:** Redis shows an extra
internal key per tag (`quotesapi:__MSFT_HCT__quotes`) that HybridCache
uses to track the tag's invalidation generation - a small, fixed
overhead, not one that grows with the number of page/size keys tagged
under it.

Verified live, not just asserted from the API existing:
- Warmed `page=1&size=100` (28 quotes, confirmed cached - a repeat
  request cost 0 DB queries). POSTed a new quote. Re-requested the
  **identical** key: now 29 quotes, the new one visible, and - critically
  - the request that revealed this **did** cost exactly 1 fresh DB
  query, proving the entry was actually invalidated and re-populated,
  not that the old cached value happened to already contain the new row.
- Same proof for DELETE: 29 → 28, deleted quote gone, one fresh DB query.

## Failure isolation: Redis going away doesn't take the API down

Stopped the `redis-cache` container mid-run and made two requests: one
for an already-L1-hot key (still served, 0 DB queries - L1 doesn't need
L2 to answer), and one for a brand-new key (still served, `200`, fell
straight through to the database since neither tier could help). Both
succeeded. The app log shows HybridCache's *own* internal handling of
this, not silence:

```
[ERR] Cache backend read failure.
StackExchange.Redis.RedisConnectionException: ...
[ERR] Cache backend write failure.
StackExchange.Redis.RedisConnectionException: ...
```

These are logged and swallowed inside HybridCache/the Redis cache
implementation - they never reach the endpoint as an exception. A cache
outage degrades to "L1 only, or straight to the database" - it does not
degrade to a 500.

## Measurement

### Stampede protection - the deliverable

Cold cache (Redis `FLUSHALL` + app process fully restarted, confirmed via
`ps` that no old process instance survived), 60 concurrent requests
(`day-21/load_test.py`, `aiohttp`, all 60 tasks created before any
`await` so they're genuinely concurrent, not a sequential loop) against
the identical `page=1&size=10` key:

```
requests:          60 (200 OK: 60)
db hits (quotesQueries): 1
cache hit rate:    98.3%
```

**Exactly 1** - not an assumption, the actual value read back from
`/api/diagnostics/db-hits` after the burst. Without stampede protection
this would be 60; `HybridCache.GetOrCreateAsync` coalesced all 60
concurrent misses onto a single in-flight factory call.

### A real, investigated latency surprise - reported as observed, not smoothed over

The very first measurement produced a counterintuitive result: the
cached run's p50 (110ms) was *higher* than the uncached baseline's
(24ms). Rather than report that number as "caching is slower" without
explanation, or quietly rerun until a better number appeared, I
investigated: reran the identical cold-cache stampede against a
*different* fresh key in the *same, now-warm* process.

```
Run                                          | p50 (ms) | p99 (ms) | DB hits
--------------------------------------------|----------|----------|--------
1st cache fill in a FRESH process           |   110.48 |   111.23 |    1
2nd cache fill, same process (Redis warm)   |     9.57 |    10.50 |    1
```

**The one-time cost is Redis connection establishment, not the cache
itself.** `StackExchangeRedisCache` connects to Redis lazily on first
use; the *first* `GetOrCreateAsync` miss in a fresh process is also the
moment that TCP/RESP handshake happens, and all 59 other concurrent
callers are waiting on that same single in-flight call. Once the
connection exists, a second genuine cold-key miss (still exactly 1 DB
hit) is faster than the uncached baseline, not slower. This is a real,
measured property of adding a *distributed* cache tier - a raw SQLite
connection (a local file open) has no equivalent warm-up cost, so the
comparison isn't perfectly symmetric on process startup specifically.
Reported plainly rather than hidden because it's the honest answer to
"is this actually faster," not just "does it reduce DB hits."

### Before / after

All rows: 60 concurrent requests, genuinely cold start each time (Redis
`FLUSHALL` + full process restart for every row except the noted
already-warm rerun).

| Run | DB hits | Cache hit rate | DB queries/sec | p50 | p99 |
|---|---|---|---|---|---|
| **Before** (`Caching:Enabled=false`, no cache at all) | 60 | 0% | 2301.85/s | 24.21 ms | 24.86 ms |
| **After**, literal cold app start (Redis connection cost included) | 1 | 98.3% | 8.93/s* | 110.48 ms | 111.23 ms |
| **After**, steady state (Redis already connected, still a genuine per-key cache miss) | 1 | 98.3% | 82.02/s* | 9.57 ms | 10.50 ms |

`*` "queries/sec" is `db_hits / wall_time`; with `db_hits = 1` this is a
single occurrence, not a sustained rate - included for completeness, not
because it's a meaningful throughput figure the way the 2301.85/s "before"
number is (that one really is 60 real queries served in 26ms).

**The number that matters most - database load - dropped from 60 queries
to 1 for the identical burst, a 60x reduction**, which is what stampede
protection is actually for: protecting the database from a thundering
herd, not necessarily making any single request faster. Whether an
individual request also gets faster turned out to depend on whether the
Redis connection was already warm, which the table above states plainly
rather than picking whichever number looked better.

## What could not be verified

- **Cross-instance L1 staleness** (the scenario `LocalCacheExpiration`'s
  short TTL is actually for) wasn't reproduced - this app runs as a
  single instance, so there's no second instance's L1 to observe serving
  a stale entry after another instance's write. The reasoning is stated
  plainly as a property of the mechanism (no cross-instance invalidation
  signal for L1), not as something demonstrated live.
- **Sustained/soak load** - all measurements are single 60-request bursts
  against a cold cache, per the brief. Steady-state throughput under
  continuous mixed read/write traffic, cache memory growth in L1 over a
  long run, and Redis memory usage at scale were not measured.
- **The exact SQLite vs. Redis-connection-cost tradeoff on a busier
  dataset** - this database has 27-30 rows; the "before" baseline's very
  low latency (24ms for 60 concurrent SQLite reads) is partly a property
  of how little data and contention exists here. A larger dataset or a
  real concurrent-writer workload could change how favorably caching
  compares, and wasn't tested.
