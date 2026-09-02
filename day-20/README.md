# Day 20 — Transactional outbox in QuotesApi

All code lives in [`day-5/QuotesApi`](../day-5/QuotesApi) — this folder
holds only the write-up.

## The bug this fixes

Since Day 19, `POST /api/quotes` saved the `Quote` row, then called
`QuoteCreatedPublisher.PublishAsync` inline, in the same request but as a
**separate system with no shared transaction**. A crash between those two
steps — the database commit succeeding, the publish never happening, or
the reverse — lost the event silently. Nothing anywhere recorded that a
`QuoteCreated` message was ever supposed to exist for that quote. The
outbox pattern turns "two independent writes, hope both happen" into
"one database transaction, plus a durable, recoverable to-do list."

## Design

**New — `day-5/QuotesApi/Models/OutboxMessage.cs`** and migration
`AddOutboxMessages`: `Id`, `MessageType`, `Payload` (serialized
`QuoteCreatedMessage`), `MessageId` (the *same* deterministic id Day 19's
publisher derives — `quote-created:{QuoteId}` — carried here so consumer
dedupe on `(SubscriptionName, MessageId)` still works unchanged),
`OccurredAt`, `ProcessedAt` (nullable — null means unprocessed, the
relay's query target), `AttemptCount`, `LastError`, `NextAttemptAt`
(backoff gate). Indexed on `ProcessedAt`.

**New — `day-5/QuotesApi/Messaging/IQuoteEventPublisher.cs`** — a small
interface `QuoteCreatedPublisher` now implements. Exists so
`OutboxRelay` depends on an abstraction rather than the concrete Service
Bus client — the same reason this codebase already has
`IQuoteRepository`/`ICollectionRepository`. This is also what made
verification possible without a live broker (below): a fake
implementation can stand in for it without touching the relay.

**New — `day-5/QuotesApi/Messaging/OutboxRelay.cs`** — a
`BackgroundService` that polls for unprocessed rows, publishes each via
`IQuoteEventPublisher`, and stamps `ProcessedAt`. Same scope discipline
as Days 18/19: registered as a singleton (`AddHostedService`), so its
constructor holds only `IServiceScopeFactory` — never a scoped
`QuotesDbContext`/`IQuoteRepository` directly — and a fresh scope is
created per poll batch, not once at startup.

**Edited — `day-5/QuotesApi/Extensions/EndpointExtensions.cs`:**
`POST /api/quotes` no longer calls a publisher at all. It writes the
`Quote` row and the `OutboxMessage` row in **one transaction**:

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

var created = await repo.AddAsync(quote, ct);   // Quote insert + its own SaveChangesAsync

db.OutboxMessages.Add(new OutboxMessage { ... });
await db.SaveChangesAsync(ct);                  // OutboxMessage insert

await tx.CommitAsync(ct);                       // both, or neither
```

`db` here (`QuotesDbContext`, injected directly into the endpoint
alongside `IQuoteRepository`) is the **same scoped instance** `repo`
uses internally — both resolve from the same per-request DI scope. So
`repo.AddAsync`'s own internal `SaveChangesAsync` runs *inside* the
transaction opened above, not a separate one; nothing in
`Repositories/QuoteRepository.cs` needed to change. This is a real
transaction, not two independent `SaveChangesAsync` calls that happen to
run near each other - the whole point.

**Edited — `Extensions/InfrastructureExtensions.cs`:** registers
`IQuoteEventPublisher` → the same singleton `QuoteCreatedPublisher`
instance as before, and adds `AddHostedService<OutboxRelay>()`.

## Publish and mark-sent are NOT atomic — and that's by design

Inside `OutboxRelay`, for each pending row:

```csharp
await publisher.PublishAsync(message, ct);   // step 1

row.ProcessedAt = DateTimeOffset.UtcNow;      // step 2
await db.SaveChangesAsync(ct);                //   (separate commit)
```

If the process dies between step 1 and step 2, Service Bus has already
received the message, but the outbox row still shows unprocessed. The
next poll picks the same row up and republishes it. **This is
at-least-once delivery, stated plainly, not presented as a flaw or
quietly worked around** — the fix isn't "make this atomic too" (you
cannot get atomicity between a database commit and a network call to a
separate system without a distributed transaction, which Service Bus and
SQLite don't support between them anyway); the fix is that **it's safe
*because* Day 19's `AuditSubscriptionWorker` and
`StatsSubscriptionWorker` already dedupe on `(SubscriptionName,
MessageId)`.** A republish of an already-handled `MessageId` is a
no-op on the consumer side, not a duplicate `AuditLog` row. The outbox
guarantees the message is never *lost*; Day 19's consumer-side dedupe is
what makes an occasional *duplicate* delivery harmless. Neither one
alone is sufficient — this is why the two days' work compose.

## Concurrency: single-instance-only, and why

Two `OutboxRelay` instances polling the same table with the plain
`WHERE ProcessedAt IS NULL` query would both fetch and both publish the
same rows. This implementation does **not** claim rows before
publishing, and is explicitly single-instance-only as a result.

**Why not just add a claim step?** The natural fix — `UPDATE
OutboxMessages SET ClaimedBy = @instanceId WHERE Id = @id AND ProcessedAt
IS NULL AND ClaimedBy IS NULL`, then only publish if the update affected
exactly one row — depends on the database actually serializing
concurrent writers so that check-and-set is atomic across processes.
**SQLite doesn't give this cleanly:** there's no `SELECT ... FOR UPDATE`
row-level locking, and SQLite's single-writer model means two processes
attempting that `UPDATE` at once don't race at the row level - the
second writer simply blocks on the whole database file (or hits
`SQLITE_BUSY`) until the first transaction releases, which serializes
correctly but only because *everything* serializes, not because SQLite
offers a real per-row claim primitive. It would probably work by
accident for this table's write volume, but "works because contention
never gets bad enough to expose it" isn't the same claim as "correctly
handles two instances." A real fix needs either a database that supports
genuine row-level locking (`SELECT ... FOR UPDATE SKIP LOCKED` in
Postgres, `UPDLOCK, READPAST` in SQL Server) so a claim step is a real
guarantee and not a hopeful `UPDATE`, or moving off polling entirely
(e.g., each instance owns a disjoint partition of rows). Neither was
built here - this relay is one instance, and that's a stated limitation,
not an oversight.

## Failure handling

Each row's publish attempt is wrapped in its own `try/catch` **inside**
the batch's `foreach` loop - one row's exception is caught, logged, and
the loop moves on to the next row. This is the same principle as Day
18/19's per-item exception handling: nothing here can let one failing
row abort the batch.

**Retry with capped exponential backoff, plus a hard stop:**
`AttemptCount < MaxAttempts` (5) is part of the poll query itself - once
a row has failed 5 times, it stops being picked up automatically. It's
not silently dropped: `ProcessedAt` stays null and `LastError` holds the
last failure, so the row remains visible and diagnosable, distinct from
either "pending" or "sent." Between attempts, `NextAttemptAt` is pushed
out by `min(60, 2^attempt)` seconds (2s, 4s, 8s, 16s, 32s) so a
persistently-failing row is retried with growing spacing instead of
being hammered every 5-second poll. Both halves of "must not spin
forever" are covered by these together: the backoff stops it from
spinning *fast*, the attempt cap stops it from spinning *forever*.

## Verification

**The Service Bus emulator was not running in this environment** (image
pull blocked by disk constraints, per the brief) — every scenario below
was driven against a **temporary fake `IQuoteEventPublisher`**
(`FakeQuoteEventPublisher`, deleted after verification, never committed),
made possible specifically because `OutboxRelay` depends on the
`IQuoteEventPublisher` interface rather than the concrete Service Bus
client. The fake's only behavior: log what it "published," and throw for
any `QuoteId` listed in a file at `/tmp/outbox-poison-quote-ids.txt` -
controllable from outside the running process without a restart, so
specific rows could be made to fail on demand. Day 19's own verification
(a separate session) already proved the real `QuoteCreatedPublisher` and
consumers work against the real emulator; this task's job is proving the
outbox's transactional/crash-recovery guarantees, which are independent
of whether the thing on the other end of `IQuoteEventPublisher` is real
or fake - the interface boundary is exactly where that independence
comes from.

**A real bug was caught and fixed along the way**, worth stating plainly:
the first version of the relay's poll query
(`m.NextAttemptAt == null || m.NextAttemptAt <= now`) threw
`InvalidOperationException: ... could not be translated` at runtime -
the SQLite EF Core provider doesn't reliably translate range comparisons
(`<=`, `<`, `>=`, `>`) on `DateTimeOffset` columns, a known provider
limitation (it's stored as offset-aware ISO8601 text, not a native
sortable datetime type). Fixed by materializing the
`ProcessedAt`/`AttemptCount` filter in SQL first, then applying the
`NextAttemptAt` time check client-side, in memory, after. Caught by
actually running the app and reading the exception, not assumed to work
from the code alone.

### Build and existing suite

```
dotnet build                          -> 0 errors, 4 pre-existing warnings (unrelated)
Quotes.Tests.Unit                     -> 68 passed, 0 failed
Quotes.Tests.Integration              -> 39 passed, 1 failed
```
Identical counts to Day 19's baseline (108 total, same one pre-existing
integration failure - `SeedBehaviorTests.Startup_InProductionEnvironment_DoesNotSeedTestUser`,
unrelated to this task, documented in `day-19/README.md`). Nothing new
broke.

### One transaction, both rows, `ProcessedAt` null at that instant

`POST /api/quotes` for quote 35, database queried immediately after the
`201` response, before the relay's next poll:

```
Quotes:          Id=35, Author="Outbox Timing Test"
OutboxMessages:  Id=2, MessageId=quote-created:35, ProcessedAt=(null)
```
Both rows exist together, from the same request, with the outbox row
still unprocessed - exactly the atomic, pre-publish state the pattern is
supposed to produce.

### The crash scenario - the deliverable

Publish succeeding and the process dying before the row is marked sent,
reproduced deterministically: a **temporary** 8-second delay was added
between `PublishAsync` returning and the `ProcessedAt` stamp (reverted
immediately after, confirmed by rebuilding and rerunning the full suite
again afterward - same 68/39-plus-1 as above). Ran from the built DLL
directly (signals reach the real process, not a `dotnet run` wrapper -
same lesson already learned in Days 18/19), posted quote 36, and sent
`kill -9` the instant the fake publisher's log line appeared:

```
[FakePublisher] published QuoteCreated for QuoteId=36, ...    <- publish succeeded
                                                                <- kill -9 sent here, milliseconds later
                                                                <- process confirmed dead, no graceful-shutdown log line
```

**Row state immediately after the crash:**
```
Id  MessageId          ProcessedAt  AttemptCount  LastError
4   quote-created:36   (null)       0             (empty)
```
The publish genuinely succeeded (the fake logged it), but the row still
shows completely unprocessed - not "failed," not "in progress," just
still sitting there, exactly as it would if the request had never been
picked up at all. **The message is recoverable, not lost** - nothing
about this state is distinguishable from "never attempted," which is
precisely why the relay's next pass republishes it rather than needing
special crash-recovery logic of its own.

Quote 36's own row was unaffected (that transaction had already committed
cleanly, before the relay ever touched it) - confirming the crash
happened where intended, in the relay, not the original request.

### Restart, and the same row is republished and stamped

The temporary delay was reverted before this step (rebuilt, confirmed
clean). Started a fresh instance:

```
[FakePublisher] published QuoteCreated for QuoteId=36, ...    <- SECOND publish, proves genuine republish
13:22:15 [INF] Outbox row 3 published and marked sent (MessageId=quote-created:36).
```
```
Id  MessageId          ProcessedAt                        AttemptCount
3   quote-created:36   2026-09-02 07:52:15.010526+00:00   0
```
Two `[FakePublisher] published` log lines for the identical `MessageId`
- this is a genuine republish of the same at-least-once event, not the
old attempt somehow resuming - and the row is now correctly marked sent.

### Forced transaction failure - atomicity in both directions

Temporarily inserted `throw new InvalidOperationException(...)` between
`db.OutboxMessages.Add(...)` and its `SaveChangesAsync` (reverted
immediately after, confirmed clean rebuild + full suite rerun):

```
Quotes count BEFORE:          30 (max id 36)
OutboxMessages count BEFORE:  3

POST /api/quotes  ->  500 {"detail":"TEMP: forced transaction-failure verification only..."}

Quotes count AFTER:           30 (max id 36, unchanged)
OutboxMessages count AFTER:   3 (unchanged)
SELECT * FROM Quotes WHERE Author = 'Should Not Exist'  ->  (no rows)
```
The `Quote` insert had already run (via `repo.AddAsync`) before the
forced failure - but because it shares the same uncommitted transaction,
the exception rolling that transaction back on `Dispose` (via `await
using`) undid it too. No orphaned `Quote` row, no orphaned `OutboxMessage`
row - atomicity holds in both directions, not just "outbox row implies
quote row" but also "no outbox row means no quote row either."

### A failing row doesn't block later rows

Poisoned quote 37 in advance (`echo 37 > /tmp/outbox-poison-quote-ids.txt`),
then posted 37 (poisoned) immediately followed by 38 (normal):

```
13:23:43 [ERR] Outbox row 4 failed on attempt 1/5 - retrying after 00:00:02.
[FakePublisher] published QuoteCreated for QuoteId=38, ...
13:23:43 [INF] Outbox row 5 published and marked sent (MessageId=quote-created:38).
13:23:48 [ERR] Outbox row 4 failed on attempt 2/5 - retrying after 00:00:04.
```
Row 38 was published and marked sent in the **same batch** where row 37
first failed - the failing row never blocked it. Backoff growing exactly
as designed (2s, then 4s) on row 37's repeated failures.

**Left running to confirm the "must not spin forever" half too:**
```
13:24:03 [ERR] Outbox row 4 failed on attempt 4/5 - retrying after 00:00:16.
13:24:23 [ERR] Outbox row 4 failed on attempt 5/5 - giving up, will not retry automatically.
```
```
Id  MessageId          ProcessedAt  AttemptCount  LastError
4   quote-created:37   (null)       5             Simulated publish failure for quote 37 (Day 20 verification).
```
Confirmed no 6th attempt ever fires (waited past another backoff window,
checked the log) - the row stays visible, diagnosable via `LastError`,
and permanently excluded from the poll query once `AttemptCount` reaches
`MaxAttempts`, rather than retrying forever at even a slow rate.

## What could not be verified

- **Nothing in this task was exercised against the real Service Bus
  emulator** - it wasn't running, per the brief's own constraint that
  verification must not depend on a live broker. Day 19's separate
  verification session already proved `QuoteCreatedPublisher` and the two
  consumer subscriptions work against the real emulator; what's proven
  here is the outbox's transactional and crash-recovery behavior
  upstream of that, via the `IQuoteEventPublisher` interface boundary -
  the two are independent claims, and only the first was checked with a
  real broker, in a prior session, not this one.
- **True multi-instance contention on `OutboxMessages`** wasn't
  reproduced - the design is stated as single-instance-only precisely
  because SQLite doesn't offer a real primitive to test a genuine claim
  race against, not because the race was tried and found safe.
- **A crash during the transaction itself** (mid-`SaveChangesAsync`, not
  via a deliberate `throw` before it) wasn't reproduced with a real
  process kill - the forced-exception test above exercises the same
  rollback mechanism (`await using`'s implicit rollback on an
  unhandled exception), but a literal `kill -9` mid-write would be
  testing SQLite's own crash durability more than this application's
  logic, and wasn't attempted.
