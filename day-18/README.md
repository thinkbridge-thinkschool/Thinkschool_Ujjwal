# Day 18 — Background job processing in QuotesApi

All code lives in [`day-5/QuotesApi`](../day-5/QuotesApi) — this folder
holds only the write-up.

## The brief

`POST /api/quotes` should return `201` immediately, without waiting on
anything beyond the quote insert itself. Add a background job that writes
an audit entry (quote id, the creator's user id, a UTC timestamp) after
the response has already gone out, using an in-process `Channel<T>` queue
and a `BackgroundService` — no Hangfire, no new dependency. This app runs
on Azure App Service **F1** (free tier): the process restarts routinely
(idle timeout, redeploys, platform maintenance) and SQLite isn't
guaranteed persistent, which matters directly to the design below and to
the judgment-call draft at the end.

## What was built

**New: `day-5/QuotesApi/BackgroundJobs/`**
- `IBackgroundTaskQueue.cs` — the queue abstraction: `TryEnqueue` (never
  blocks) and `DequeueAsync` (only the worker calls this).
- `ChannelBackgroundTaskQueue.cs` — the `Channel<T>`-based implementation.
  Bounded, capacity configurable via `BackgroundQueue:Capacity` (default
  100).
- `AuditLogWorker.cs` — the `BackgroundService` that drains the queue.

**New: `day-5/QuotesApi/Models/AuditLog.cs`** — the entity written by the
worker.

**New migration:** `20260831063236_AddAuditLog.cs` — adds the
`AuditLogs` table (`Id`, `QuoteId`, `CreatedByUserId`, `CreatedAt`).
Applied locally via `dotnet ef database update`; **not** applied to the
deployed Azure database — this task's constraints said not to touch the
deployed configuration, so the live App Service still runs on the schema
from before this change until someone explicitly deploys it.

**Edited:**
- `Data/QuotesDbContext.cs` — added `DbSet<AuditLog> AuditLogs`.
- `Extensions/InfrastructureExtensions.cs` — registers
  `IBackgroundTaskQueue` as a singleton and `AuditLogWorker` via
  `AddHostedService`.
- `Extensions/EndpointExtensions.cs` — `POST /api/quotes` now enqueues an
  audit work item after `repo.AddAsync` succeeds, then returns `201`
  without awaiting it. Nothing else about the endpoint changed: same
  validation, same `201` body, same authorization policy.

## The scope trap

`AddHostedService<AuditLogWorker>()` registers `AuditLogWorker` as a
**singleton** — that's how `BackgroundService` always runs, for the whole
process lifetime. `IQuoteRepository` and `QuotesDbContext` are
**scoped**. If `AuditLogWorker`'s constructor took `IQuoteRepository`
directly, DI would either throw at startup (with scope validation
enabled) or — worse, if validation is off — hand it one `DbContext`
instance that lives for the entire process, shared and reused across
every job ever run. `DbContext` isn't thread-safe and isn't meant to
outlive a single unit of work; a captive instance like that would corrupt
state under any concurrency and never pick up schema/connection changes
made through a fresh context.

The fix: `AuditLogWorker`'s constructor only takes `IBackgroundTaskQueue`,
`IServiceScopeFactory`, and `ILogger<AuditLogWorker>` — all three are
themselves singleton-safe. Inside `ExecuteAsync`, a **fresh scope is
created per work item** (`_scopeFactory.CreateScope()`), and the scoped
`QuotesDbContext`/`IClock` are resolved from *that* scope, then disposed
when the `using` block ends. Every job gets its own short-lived
`DbContext`, exactly like a normal HTTP request would.

## Bounded channel: capacity and full-mode

`ChannelBackgroundTaskQueue` wraps `Channel.CreateBounded<T>` — not
`CreateUnbounded`. An unbounded queue under sustained load doesn't fail
loudly, it just grows until the process runs out of memory; on a 1GB F1
instance that's a slow, silent way to take the *entire app* down over a
job that was only ever meant to be a nice-to-have audit trail.

**Capacity: 100** (configurable via `BackgroundQueue:Capacity`). This is
a demo API on a free tier with realistically light traffic — 100
in-flight audit writes is generous headroom for a burst without letting
the queue grow unbounded.

**`FullMode: Wait`, paired with `TryWrite` (never `WriteAsync`) at the
call site.** This combination matters and is worth spelling out, because
`Wait` mode's usual meaning ("block until space is free") is exactly what
the endpoint must *not* do:

- `TryWrite` never blocks, in *any* `FullMode` — it either succeeds
  immediately or returns `false` immediately. The endpoint calls
  `TryEnqueue`, which calls `TryWrite`, so `POST /api/quotes` can never be
  the one waiting for queue space, regardless of which `FullMode` is
  configured.
- What `FullMode` actually changes is what happens *when full*:
  `DropWrite`/`DropNewest`/`DropOldest` all make `TryWrite` return `true`
  unconditionally (something gets silently discarded internally to make
  room), while `Wait` makes `TryWrite` return `false` when there's no
  room, since the only way `Wait` mode makes room is by waiting, which
  `TryWrite` refuses to do.
- That `false` is the whole reason `Wait` was chosen over a drop mode: it
  gives the caller an explicit, observable signal. `EndpointExtensions.cs`
  logs a warning (`"Audit queue full - dropped audit entry for quote
  {QuoteId}."`) whenever `TryEnqueue` returns `false`. With a drop mode,
  the queue would discard silently and `TryEnqueue` would still report
  success — an audit-logging feature with unobservable data loss in its
  *own* mechanism is a bad trade for the debuggability it gives up.
- Either way, a full queue is **never** surfaced to the HTTP client. The
  quote was already created successfully; losing the audit trail entry
  for it is a server-side concern, not a reason to turn a successful
  write into a client-visible failure.

## Graceful shutdown

`AuditLogWorker.ExecuteAsync(CancellationToken stoppingToken)` uses
**two different cancellation tokens for two different things**, and the
distinction is the entire mechanism:

```csharp
while (true)
{
    // stoppingToken: governs ONLY whether we start waiting for a NEW item.
    workItem = await _queue.DequeueAsync(stoppingToken); // throws + loop breaks once cancelled

    // A fresh, INDEPENDENT token - NOT stoppingToken, NOT derived from it.
    using var workItemCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var scope = _scopeFactory.CreateScope();
    await workItem(scope.ServiceProvider, workItemCts.Token);
}
```

- **`stoppingToken`** is what the host cancels when it starts stopping
  (`Ctrl-C`, `SIGTERM`, an App Service restart). It's only ever awaited at
  the top of the loop, in `DequeueAsync`. The moment it's cancelled, that
  wait throws `OperationCanceledException`, the loop breaks, and
  `ExecuteAsync` returns — **no new item is ever dequeued after shutdown
  starts.**
- **The in-flight item's token is deliberately a different, fresh
  `CancellationTokenSource`, not `stoppingToken` and not linked to it.**
  If it *were* `stoppingToken`, the instant shutdown began, a write that
  was already mid-`SaveChangesAsync` would get cancelled out from under
  it — an aborted write is worse than a slightly slower shutdown; a
  half-committed audit row (or a confusing exception on every shutdown)
  is a strictly worse failure mode than waiting a few more seconds. The
  independent 10-second timeout still exists so one genuinely stuck item
  can't block shutdown forever, but that bound has nothing to do with
  *when* shutdown was requested.
- `BackgroundService`'s own `StopAsync` (inherited, not overridden) waits
  for `ExecuteAsync`'s task to finish, up to the host's own shutdown
  timeout — so as long as the in-flight item finishes within that window,
  the process genuinely waits for it before exiting. This was verified
  directly, not assumed — see Verification below.

## Exception handling

Each work item runs inside its own `try/catch` inside the loop:

```csharp
try
{
    using var scope = _scopeFactory.CreateScope();
    await workItem(scope.ServiceProvider, workItemCts.Token);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Background work item threw and was discarded; worker continues.");
}
```

This is the difference between "one job fails" and "the entire background
pipeline silently dies." `ExecuteAsync` is a `Task` that `BackgroundService`
tracks; if an exception ever escaped this loop, that task would fault,
`ExecuteAsync` would end, and the queue would keep accepting new items
from `TryEnqueue` (the writer side has no idea the reader stopped) while
nothing ever drains them again — a leak with no error message pointing at
the cause. Catching per-item keeps the loop itself un-killable by any
single job's failure.

## Configuration

New optional setting, `BackgroundQueue:Capacity` (int, default `100`) —
not present in `appsettings.json`, so the default applies unless
explicitly overridden (locally via user-secrets/environment, or in
Azure via an Application Setting `BackgroundQueue__Capacity`, not set
here since this task doesn't touch the deployed configuration).

## Verification

Backend run locally (`dotnet run`, port 5296) against the real local
SQLite database with the new migration applied. Everything below is what
was actually observed — commands run, logs read, database queried — not
inferred from the code.

### Build and existing tests

```
dotnet build          -> Build succeeded, 0 errors (4 pre-existing warnings, unrelated to this change)
Quotes.Tests.Unit        -> 68 passed, 0 failed
Quotes.Tests.Integration -> 40 passed, 0 failed
```
**108 total, same count as before this change** — nothing regressed.

### 201 returns before the audit row is written

Per the brief's own suggestion, a temporary 3-second delay was added to
the work item (`await Task.Delay(3000, workCt);`, immediately before the
`SaveChangesAsync`) purely to make the ordering unambiguous in the logs —
reverted immediately after this test, confirmed by rebuilding and
rerunning the full test suite again afterward (same 108/108).

```
$ time curl -X POST /api/quotes ...
HTTPCODE:201
real  0.040s   <- response returned in 40ms

Server log:
12:18:43 [INF] HTTP POST /api/quotes responded 201 in 32.4127 ms
12:18:43 [INF] Audit write starting for quote 29.
12:18:46 [INF] Audit write finished for quote 29.     <- 3 seconds AFTER the response
```

After reverting the delay, the normal-speed path was re-confirmed too
(quote 32): `responded 201 in 29.9ms`, with "Audit write starting" and
"finished" both logged in the same log line's second — SQLite writes are
fast enough that there's no visible gap without the artificial delay, but
the ordering (start-of-write logged strictly after the response line) is
identical.

### The audit row lands correctly

```
$ sqlite3 quotes.db "SELECT * FROM AuditLogs WHERE QuoteId = 29;"
Id  QuoteId  CreatedByUserId  CreatedAt
1   29       3                2026-08-31 06:48:46.099241+00:00
```
`QuoteId` matches the quote just created; `CreatedByUserId` (`3`) matches
the `sub` claim of the token used and the `createdByUserId` the API
itself returned in the `201` body for that same request.

### Throwing item survives, and shutdown behavior — how these were tested

`POST /api/quotes` is the only production trigger for an enqueue, and it
always enqueues a valid, well-formed audit write — there's no way to make
*it* enqueue a throwing item without changing endpoint behavior beyond
the enqueue call, which the brief explicitly ruled out. Rather than hack
a test-only failure path into production code, a small throwaway console
harness (not committed, not part of `day-5/QuotesApi` or `day-18/`) was
built with a project reference to the real `QuotesApi.csproj`, so it
exercises the actual `ChannelBackgroundTaskQueue` and `AuditLogWorker`
classes directly — not reimplementations or mocks of them.

**Throwing item — logged, worker survives, next item still runs:**
```
fail: QuotesApi.BackgroundJobs.AuditLogWorker[0]
      Background work item threw and was discarded; worker continues.
      System.InvalidOperationException: deliberate test failure
  [work item] second item executed successfully.
  RESULT: second item ran after a prior item threw = True
```

**Shutdown (harness, `StopAsync` called directly) — in-flight item
finishes before the worker reports stopped:**
```
  [work item] slow item starting (3s simulated write)...
  Host is stopping now (simulating Ctrl-C/SIGTERM) while the item is mid-flight...
  [work item] slow item finished.
info: AuditLogWorker stopping: shutdown requested, no further items will be dequeued.
info: AuditLogWorker stopped.
  RESULT: StopAsync returned after 2.5s. Item completed = True
```

### Shutdown against the real running app, with a real SIGTERM

The harness proves the worker class's own logic; this proves it end to
end in the actual process, the way the brief specifically asked
(`Ctrl-C / SIGTERM ... mid-flight`). The app was run directly from its
built DLL (not `dotnet run`, so the signal reaches the actual host
process with no wrapper in between — closer to how it runs under Kudu on
App Service), with the same temporary 8-second delay reused from the
timing test above. A quote was posted, then `kill -TERM` was sent to the
process ~2 seconds into that 8-second write:

```
12:23:46  AuditLogWorker started.
12:23:46  Audit write starting for quote 31.
12:23:48  Application is shutting down...          <- SIGTERM received here, item ~2s into an 8s write
12:23:54  Audit write finished for quote 31.        <- write completed 6s AFTER shutdown began, not aborted
12:23:54  AuditLogWorker stopping: shutdown requested, no further items will be dequeued.
12:23:54  AuditLogWorker stopped.
```

```
$ sqlite3 quotes.db "SELECT * FROM AuditLogs WHERE QuoteId = 31;"
Id  QuoteId  CreatedByUserId  CreatedAt
3   31       3                2026-08-31 06:53:54.849911+00:00
```

The row landed, with the timestamp matching the "finished" log line —
direct proof the write actually completed and committed, not just that a
log line was printed before the process died mid-write.

### Queue full — what happens to the caller

Tested at the queue level directly (harness), with **no reader
draining it** — this makes the fill deterministic rather than racing
against however fast the real worker happens to drain items, which is
the correct tool for proving the bounded-channel mechanics themselves
(the same `ChannelBackgroundTaskQueue` class the app uses), even though
it doesn't exercise the full HTTP round trip:

```
  TryEnqueue #1: accepted = True
  TryEnqueue #2: accepted = True
  TryEnqueue #3: accepted = True    <- capacity was 3
  TryEnqueue #4: accepted = False
  TryEnqueue #5: accepted = False
```

**What happens to the caller:** nothing visible. `TryEnqueue` returns
`false` immediately (never blocks), `EndpointExtensions.cs` logs a
warning, and `POST /api/quotes` still returns its normal `201` — a full
audit queue never turns a successful quote creation into a client-facing
error.

## What could not be verified

- **This was not deployed to the live Azure App Service.** The brief's
  constraints said not to touch the deployed configuration, so
  everything above was run and verified locally only. The migration
  exists in the repo but hasn't been applied to the production database.
- **A real F1-instance restart losing in-flight/queued work** was not
  observed directly (that would mean actually deploying and provoking an
  App Service restart mid-write) — this is asserted from how App Service
  free tier is documented to behave (idle-timeout recycling, no
  guaranteed process persistence) plus the fact that this queue is
  in-process memory with no persistence of its own, not from a live
  reproduction. It's the central fact driving the judgment call below,
  so it's worth being honest that it's a reasoned inference here, not a
  witnessed failure.
- **The queue-full test used a non-draining queue** for determinism
  rather than racing real HTTP load against the live worker's drain
  rate — a live-load version of this test would be flakier (dependent on
  exact timing) without proving anything the deterministic version
  doesn't already prove about the same underlying class.

---

## The judgment call: Hangfire vs. an in-process hosted service

**DRAFT — written to be rewritten, not shipped as-is.** The goal here is
naming concrete, testable signals; the exact wording and thresholds are
a first pass.

**Move from an in-process `Channel<T>` + `BackgroundService` queue to
Hangfire (or another durable job runner) once at least one of these
becomes true for a given job — not as a blanket rule for the whole app,
since different jobs in the same app can sit on different sides of this
line:**

1. **The job's result must survive a process restart.** An in-process
   queue lives in memory; anything still queued, or already dequeued but
   not yet committed, is gone the instant the process restarts — no
   record, no retry, no error surfaced anywhere. This is not a
   theoretical concern for *this* app: it runs on Azure App Service F1,
   which restarts routinely (idle-timeout recycling, every redeploy,
   platform maintenance), documented plainly in
   [`day-17/README.md`](../day-17/README.md)'s F1-tier caveats. Any job
   whose correctness depends on "this always eventually happens" fails
   this test on this specific hosting tier, full stop.
2. **The job must retry automatically after a failure**, not just log
   and move on. This worker's own exception handling (see above) is
   deliberately terminal per item: log it, discard it, keep the loop
   alive for the *next* item. There is no retry, no backoff, no dead-letter
   queue. Hangfire persists job state to a durable store and retries with
   configurable policy built in.
3. **Scheduled or recurring execution is needed** (cron-like: "run this
   nightly," "run this every 5 minutes"), not just "run this once, after
   this specific request." This queue only knows about work items a
   request handler chose to enqueue; it has no concept of time-based
   triggering at all.
4. **Multiple instances need one shared queue**, not one disconnected
   queue per process. This queue is a singleton inside one process's
   memory; scale to 2+ instances and each has its own separate queue with
   no coordination — at best, duplicated work if the same logical job
   gets enqueued from more than one instance; at worst, no way to know
   which instance is even supposed to be handling what.
5. **The job needs its own observability** — a dashboard of
   pending/succeeded/failed jobs, the ability to inspect or manually
   retry one specific failure. This queue offers exactly what gets
   written to `ILogger` and nothing else: no persisted job history
   outside the application's own logs, no UI.

**Where this app's audit job actually sits, right now:** only criterion
1 is even close to true — F1 really does restart routinely, and this
queue really does lose everything unpersisted when that happens. But the
job itself was deliberately scoped as a best-effort, "nice to have this
trail if it survives" feature — not a compliance record, not something
anything else in the app depends on being complete. None of 2–5 apply:
no stated retry requirement, nothing scheduled/recurring, a single F1
instance can't scale out in the first place (so criterion 4 is currently
*unreachable*, not just unmet), and `ILogger` output is sufficient
observability for a job at this stage of the project.

**Conclusion (draft):** the in-process queue is an acceptable, deliberate
tradeoff for *this specific job* on *this specific hosting tier* today —
but it's a closer call than the "0 of 5 criteria met" framing from Day
17's SWA writeup might suggest, precisely because criterion 1 is not
hypothetical here; it's the documented, expected behavior of the tier
this API actually runs on. The line that should flip this decision isn't
"the app grows" in some general sense — it's specifically **the moment
this audit trail stops being a nice-to-have and something starts
depending on it being complete** (e.g., it's ever used to answer "who
actually deleted this quote" in a dispute, rather than just being
informational). That would make criterion 1 alone sufficient
justification for Hangfire, regardless of whether 2–5 are ever true,
because F1's restart behavior makes silent data loss a near-certainty
over time on this tier, not a remote edge case worth accepting.
