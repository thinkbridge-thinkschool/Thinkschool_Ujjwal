# Day 19 — Service Bus topics, competing consumers, dedupe, DLQ

All code lives in [`day-5/QuotesApi`](../day-5/QuotesApi) — this folder
holds only the write-up. Emulator-backed, not documentation-only: every
claim below that's marked confirmed was actually run against a live
Azure Service Bus emulator container, not inferred from docs alone.

**Status: in progress.** This document is being written incrementally as
the work happens, not assembled at the end — the sections below reflect
what's actually been done so far.

## The brief

Day 18 added an in-memory `Channel<T>` queue and a `BackgroundService`
that writes an `AuditLog` row after `POST /api/quotes` succeeds. Day 19
replaces that in-memory queue with Azure Service Bus: a topic with two
independent subscriptions (`audit`, `stats`), competing consumers,
persisted idempotency (not an in-memory `HashSet`), and dead-lettering
for poison messages — using the **Azure Service Bus emulator** (Docker),
since the real subscription has no usable credit and Service Bus has no
free tier. No Azure resource was created; `azd` was never run.

## Phase 0 — feasibility check

Approved to proceed after this report. Full findings:

### 1. Can the emulator run here?

**Yes, confirmed feasible** (Docker installed, both required images have
native `linux/arm64` manifests for this Apple Silicon Mac — verified via
`docker manifest inspect`, no x86 emulation needed).

- **Image:** `mcr.microsoft.com/azure-messaging/servicebus-emulator:latest`
  (~86MB compressed).
- **Required companion:** `mcr.microsoft.com/azure-sql-edge:latest`
  (~660MB) — the emulator uses it as its metadata backing store. Not
  optional; confirmed from Microsoft's own `docker-compose-default.yml`
  template (two services, `emulator` depends on `sqledge`).
- **Config file requirement confirmed true:** per Microsoft's docs, the
  emulator has no admin API for creating entities dynamically — a
  `Config.json` mounted at container start, declaring every
  queue/topic/subscription up front, is the only way. *"Changes aren't
  honored on the fly... you must restart the container"* for any config
  change.
- **Topics, subscriptions, dead-lettering:** all confirmed present in the
  real schema (fetched from Microsoft's sample `Config.json`), including
  per-subscription `MaxDeliveryCount`, `LockDuration`,
  `DeadLetteringOnMessageExpiration`. A subscription with no filter rules
  receives every message published to the topic — exactly the fan-out
  shape `audit` + `stats` need.
- **One documented limitation that matters here:** *"After a container
  restart, data and entities don't persist in the emulator."* This is
  about the **emulator container** restarting, not the .NET application
  process — restarting the consumer process is exactly the redelivery
  scenario this exercise wants to demonstrate, and is unaffected. But it
  does mean the emulator/SQL Edge containers must stay up for the
  duration of testing, or all queued/in-flight messages are lost. Per
  your instruction, they are not being restarted between tests.

### 2. Connection string and secret handling

```
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

`SAS_KEY_VALUE` is a fixed literal the emulator expects — it doesn't
validate real SAS signatures locally. Still kept out of committed files
for consistency: this repo already has an established pattern
(`dotnet user-secrets`) for `Jwt:Key`, `Entra:Audience`,
`ApplicationInsights:ConnectionString` — none of those are in
`appsettings.json`, all live in local user-secrets. `ServiceBus:ConnectionString`
follows the same pattern.

**New dependency, approved:** `Azure.Messaging.ServiceBus` (7.20.2,
latest stable). Day 18's BCL-only rule was specific to that task and
doesn't apply here.

### 3. Day 18 wiring being replaced

- `day-5/QuotesApi/BackgroundJobs/IBackgroundTaskQueue.cs` — queue
  abstraction.
- `day-5/QuotesApi/BackgroundJobs/ChannelBackgroundTaskQueue.cs` —
  `Channel<T>` implementation.
- `day-5/QuotesApi/BackgroundJobs/AuditLogWorker.cs` — the
  `BackgroundService` draining it.
- Enqueue call: `day-5/QuotesApi/Extensions/EndpointExtensions.cs`,
  inside `POST /api/quotes`, right after `repo.AddAsync`.

### 4. AuditLog table and the dedupe key

Existing `AuditLogs` schema (`Id, QuoteId, CreatedByUserId, CreatedAt`)
has no `MessageId` column and no unique index beyond the PK — a new
table is required.

**Design correction made at this stage, before any code was written:**
dedupe cannot be keyed on `MessageId` alone in a table shared across both
subscriptions. One publish fans out to `audit` and `stats` independently
— each subscription's consumer legitimately sees the same `MessageId`
once. A single global `ProcessedMessages(MessageId)` table would let
whichever subscription processes first "claim" that ID and cause the
other subscription's genuine first-time delivery to be skipped as a false
duplicate. The dedupe table's unique index is
**`(SubscriptionName, MessageId)`**, not `MessageId` alone.

### Empirical proof — emulator actually started and worked

Before any application code, per your first condition. A standalone
console harness (`Azure.Messaging.ServiceBus`, not part of the app) was
run against the real running emulator, and this is its real output:

**Deviation from Microsoft's documented compose template, and why it
worked:** Microsoft's `docker-compose-default.yml` bundles a dedicated
`azure-sql-edge` container as the emulator's metadata backing store.
That image is ~660MB and, in this sandbox, was pulling at roughly
166 KB/s — over an hour with no end in sight. Pointed the emulator at an
**already-running `mssql/server:2022-latest` container** from earlier
work instead (`SQL_SERVER: host.docker.internal:1433`, reached via the
container's already-published host port rather than a shared custom
Docker network). This is not what Microsoft documents, and it isn't
guaranteed to keep working across emulator versions — but it worked
without any friction: the emulator's own startup log shows it dropping
and recreating `SbGatewayDatabase` and `SbMessageContainerDatabase00001`
on that regular SQL Server instance, no errors, no compatibility
complaints, entities created successfully:

```
info: SQL-Setup[0]
      Creating database 'SbGatewayDatabase' at 'Data Source=host.docker.internal,1433;User id=sa;...'...
info: SQL-Setup[0]
      CREATE DATABASE SbGatewayDatabase
...
info: a.G.aGX[0]
      Creating topic: quote-created
info: a.G.aGX[0]
      Creating subscription audit for topic: quote-created
info: a.G.aGX[0]
      Creating subscription stats for topic: quote-created
...
info: a.G.aGp[0]
      Emulator Service is Successfully Up! ; Use connection string: "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;".
```

**Test A — one publish, both subscriptions receive their own copy:**
```
Sent MessageId=probe-fanout-ac5b78b6b6554285a6f83f612195dd5a to topic 'quote-created'.
[audit] RESULT: received MessageId=probe-fanout-ac5b78b6b6554285a6f83f612195dd5a, Body={"quoteId":101,...}
[stats] RESULT: received MessageId=probe-fanout-ac5b78b6b6554285a6f83f612195dd5a, Body={"quoteId":101,...}
```

**Test B — a poison message, abandoned repeatedly, reaches the DLQ with
real dead-letter metadata:**
```
Sent poison MessageId=probe-poison-5abcc3eee4f54ed3b8f7e76277cdb89e to topic 'quote-created'.
[stats] drained its own copy of the poison message (unaffected by audit's DLQ path).
[audit] attempt 1: DeliveryCount=1 - abandoning.
[audit] attempt 2: DeliveryCount=2 - abandoning.
[audit] attempt 3: DeliveryCount=3 - abandoning.
[audit] attempt 4: no message available (already dead-lettered).

DLQ evidence for 'audit':
  MessageId           = probe-poison-5abcc3eee4f54ed3b8f7e76277cdb89e
  DeliveryCount        = 4
  DeadLetterReason     = MaxDeliveryCountExceeded
  DeadLetterErrorDesc  = Message could not be consumed after 3 delivery attempts.
  Body                 = {"quoteId":999,...,"poison":true}
```
`MaxDeliveryCount: 3` (from `Config.json`) was honored exactly: 3 abandon
attempts, then automatic dead-lettering — no explicit
`DeadLetterMessageAsync` call needed for this path.

**Test C — the poison message did not block the subscription:**
```
Sent good MessageId=probe-after-poison-f54d1b38cb484183a4b50da8d2a15843 to topic 'quote-created' right after the poison message.
RESULT: received MessageId=probe-after-poison-f54d1b38cb484183a4b50da8d2a15843 normally - subscription not blocked.
```

All three proofs passed on the first run. Moving into the actual
application build now.

## Phase 1 — build

### What changed

**New — `day-5/QuotesApi/Messaging/`:**
- `QuoteCreatedMessage.cs` — the message contract (`QuoteId`,
  `CreatedByUserId`, `CreatedAtUtc`, `Poison`). `Poison` is a real field
  in the shipped contract, not a test hack — `QuoteCreatedPublisher`
  (the only production path) never sets it true; only test tooling
  publishing directly to the topic does, which is how the DLQ exercise
  gets exercised without touching endpoint behavior.
- `QuoteCreatedPublisher.cs` — wraps one long-lived `ServiceBusSender`
  and sets a deterministic `MessageId` (`quote-created:{QuoteId}`), not
  `Guid.NewGuid()` — a quote is only ever created once, so its own id is
  already a stable identity for "the QuoteCreated event for this quote."
- `AuditSubscriptionWorker.cs` — competing consumer for the `audit`
  subscription. The Day 18 audit job, moved here, with the same
  scope-per-message discipline Day 18 established (see below) plus the
  new idempotency and settlement-ordering requirements.
- `StatsSubscriptionWorker.cs` — competing consumer for the `stats`
  subscription, deliberately simpler (no transaction, no dedupe) to
  demonstrate independent fan-out.

**New — `day-5/QuotesApi/Models/ProcessedMessage.cs`** and migration
`AddProcessedMessages` — the dedupe table, unique index on
`(SubscriptionName, MessageId)`.

**New — `day-5/QuotesApi/Configuration/ServiceBusOptions.cs`** —
`ConnectionString` and `TopicName`, both `[Required]` +
`ValidateOnStart()`, matching the existing `JwtOptions`/`EntraOptions`
pattern in this codebase exactly. Set via `dotnet user-secrets` locally;
never in `appsettings.json`.

**Removed — `day-5/QuotesApi/BackgroundJobs/`** (all of Day 18's
`IBackgroundTaskQueue`, `ChannelBackgroundTaskQueue`, `AuditLogWorker`).
**Decision: removed, not kept as a parallel path.** The brief called this
a replacement, and `POST /api/quotes` can only sensibly hand off to one
mechanism — leaving Day 18's classes registered-but-unused (or, worse,
both wired in) would mean dead code or two competing audit paths writing
the same table. Day 18's design and reasoning are fully preserved in
`day-18/README.md`; that's the right place for "what we used to do," not
unused files in the current tree. The `AuditLog` entity/table itself
stays — Day 19 still writes it, just via a different mechanism.

**Edited:**
- `Data/QuotesDbContext.cs` — added `DbSet<ProcessedMessage>` and its
  unique index.
- `Extensions/InfrastructureExtensions.cs` — registers `ServiceBusClient`
  (singleton, long-lived per SDK guidance), `QuoteCreatedPublisher`, and
  both subscription workers via `AddHostedService`.
- `Extensions/EndpointExtensions.cs` — `POST /api/quotes` now publishes
  via `QuoteCreatedPublisher` after `repo.AddAsync` succeeds, instead of
  enqueueing to the old in-memory queue. A publish failure is logged, not
  surfaced to the client - same principle as Day 18's full-queue handling
  (a background-plumbing failure must never turn a successful quote
  creation into a client-visible error), just a different failure mode
  (broker unreachable vs. a bounded queue being full).

### The scope trap, again

Same rule as Day 18, restated because it's easy to get backwards with a
new SDK: `AuditSubscriptionWorker` and `StatsSubscriptionWorker` are
registered via `AddHostedService`, so they're singletons for the whole
process lifetime. Their constructors take `ServiceBusClient` and (for
`AuditSubscriptionWorker`) `IServiceScopeFactory` - never
`IQuoteRepository` or `QuotesDbContext` directly. A fresh DI scope is
created **per message**, inside the `ServiceBusProcessor` handler, not
once at startup - `ServiceBusProcessor` itself handles concurrency
(`MaxConcurrentCalls`), so a shared `DbContext` across concurrently-
processing messages would be actively unsafe, not just architecturally
sloppy.

### Idempotency: why `(SubscriptionName, MessageId)`, and the DB backstop

Covered in Phase 0 already, restated with the actual implementation: the
unique index is on `ProcessedMessages(SubscriptionName, MessageId)` -
`audit` and `stats` each get their own bookkeeping row for the same
publish, so one subscription's real first delivery is never mistaken for
the other's duplicate.

**The dedupe row and the audit work are written in the same transaction**
(`AuditSubscriptionWorker.HandleMessageAsync`): begin transaction → check
`ProcessedMessages` → if absent, insert the dedupe row *and* the
`AuditLog` row → commit. They cannot diverge - either both land or
neither does. The initial existence check is a plain read, not
`SELECT ... FOR UPDATE`; what actually closes a genuine race between two
competing consumers landing on the exact same message at once is the
**unique index itself** at the database level, not the read. (In
practice, Service Bus's own per-message lock already prevents two
receivers from holding the same message at once under normal operation -
the unique index is the defense for what happens when that invariant is
violated: redelivery after a crash, lock expiry, or anything else that
lets a message be picked up more than once.)

### Settlement ordering, stated explicitly

```
1. BEGIN TRANSACTION
2. Check ProcessedMessages for (SubscriptionName, MessageId)
3. If absent: INSERT ProcessedMessages row, INSERT AuditLog row
4. COMMIT
5. CompleteMessageAsync   <-- only after step 4 has succeeded
```

`AutoCompleteMessages = false` on `AuditSubscriptionWorker`'s processor -
settlement is entirely manual, specifically so this ordering can be
enforced. Completing **before** the transaction commits risks losing the
message outright on a crash between complete and commit (Service Bus
thinks it's done; the database never actually got the row). Completing
**after** is what makes redelivery-after-a-crash safe at all: if the
process dies between step 4 and step 5, the message is redelivered, and
the next attempt's step 2 finds the row already there and skips straight
to a no-op - which is exactly what the dedupe table is *for*. Completing
early would defeat the entire idempotency mechanism by making step 5's
failure indistinguishable from step 5 never having needed to run.

### Dead-lettering

`QuoteCreatedMessage.Poison` is the sentinel. `AuditSubscriptionWorker`
checks it and **throws, uncaught** - no `try/catch` around that check, no
explicit `DeadLetterMessageAsync` call for this path. `ServiceBusProcessor`
abandons a message on any unhandled exception from the handler
automatically; after `Config.json`'s `MaxDeliveryCount: 3` abandon
attempts, Service Bus dead-letters it on its own with reason
`MaxDeliveryCountExceeded`. Catching the exception and completing the
message anyway - even just to log it - is exactly the mistake that
silently defeats this whole exercise: the message vanishes as
"successfully processed" instead of ending up somewhere anyone would ever
look.

## Live verification - what was actually observed

Driven against the real emulator (still the same containers from Phase
0, never restarted, per your instruction) and the real running
application - not the standalone probe this time, except as the
*publisher* of specific test scenarios the HTTP API can't trigger on its
own (a duplicate delivery, a poison message). Every number below is from
an actual query or log line, pasted, not summarized from memory.

### Build and existing suite

```
dotnet build                          -> 0 errors, 4 pre-existing warnings (unrelated)
Quotes.Tests.Unit                     -> 68 passed, 0 failed  (unchanged from Day 18's baseline)
Quotes.Tests.Integration              -> 39 passed, 1 failed
```

**The one integration failure, reported honestly rather than glossed
over:** `SeedBehaviorTests.Startup_InProductionEnvironment_DoesNotSeedTestUser`
fails with `OptionsValidationException` on `ServiceBusOptions` - its
factory (`ProductionSeedTestFactory`) deliberately forces the host into
the `Production` environment (that's the point of the test), and
`Production` doesn't load user-secrets, so nothing supplies
`ServiceBus:ConnectionString`/`TopicName` for that one host, and startup
validation correctly refuses to boot. The **other 39 tests only pass
because they run in the test host's default `Development`-like
environment, which loads user-secrets - meaning they're passing because
of my own local secrets set earlier in this session, not because the
test project actually provisions Service Bus config.** On a machine
without that same local secret set, all 40 would fail the same way. This
is a real, structural gap: this task's constraints ("work only in
`day-5/QuotesApi` and `day-19/`") mean I can't fix it - the fix is in
`day-5/Quotes.Tests.Integration`'s test factories, outside this branch's
scope. Flagging it rather than working around it or leaving it silently
unmentioned.

### Fan-out - one publish, both subscriptions process their own copy

Via the real `POST /api/quotes` (quote 33):
```
12:43:09 [INF] stats: quote 33 created (MessageId=quote-created:33).
12:43:09 [INF] Audit write committed for quote 33 (MessageId=quote-created:33).
```
```
AuditLogs:          Id=5, QuoteId=33, CreatedByUserId=4
ProcessedMessages:  SubscriptionName=audit, MessageId=quote-created:33
```
`stats` logged its own receipt independently of `audit`'s transactional
write - two subscriptions, two independent deliveries, one publish.

### Dedupe - the same MessageId delivered twice, one row survives

```
Row counts BEFORE: AuditLogs=1, ProcessedMessages=1   (from the fan-out test above)
```
Published a second message under the identical deterministic MessageId
`quote-created:33` (same id `QuoteCreatedPublisher` would derive for that
quote):
```
12:44:14 [INF] stats: quote 33 created (MessageId=quote-created:33).
12:44:14 [INF] Duplicate delivery of quote-created:33 on audit (DeliveryCount=1) - skipped, already processed.
```
```
Row counts AFTER: AuditLogs=1, ProcessedMessages=1    <- unchanged
```
`stats` (no dedupe by design) logged the duplicate harmlessly again;
`audit`'s transactional check correctly recognized the dedupe row already
existed and skipped the insert.

### Dead-letter - real DLQ evidence, not asserted

Published `{QuoteId: 9999, Poison: true}` directly to the topic:
```
12:44:28 [ERR] Error on audit subscription (source: ProcessMessageCallback).
System.InvalidOperationException: Poison message sentinel triggered for quote 9999.
```
(repeated 3 times, matching `MaxDeliveryCount: 3`). Reading the DLQ
directly afterward:
```
RESULT: found in DLQ.
  MessageId          = quote-created:9999
  DeliveryCount       = 4
  DeadLetterReason     = MaxDeliveryCountExceeded
  DeadLetterErrorDesc  = Message could not be consumed after 3 delivery attempts.
  Body                = {"QuoteId":9999,"CreatedByUserId":null,"CreatedAtUtc":"...","Poison":true}
```
`AuditLogs` has zero rows for `QuoteId=9999` - the poison message never
produced a spurious audit entry.

### Poison did not block the subscription

Immediately after the DLQ evidence above, published a normal message for
quote 34:
```
AuditLogs: Id=6, QuoteId=34, CreatedByUserId=5, CreatedAt=2026-09-01 07:15:09
```
Processed normally, no delay, no manual intervention - the subscription
kept working through and after the poison message.

### Competing consumers - two full app instances, one message, handled once

Ran a second instance of the actual app (`http://localhost:5297`,
separate process, same database file, same emulator), both instances'
`AuditSubscriptionWorker`s live and listening on `audit` simultaneously.
Published one message (quote 40):

```
Instance 1 (port 5296) log:
  12:45:38 [INF] stats: quote 40 created (MessageId=quote-created:40).
  12:45:38 [INF] Audit write committed for quote 40 (MessageId=quote-created:40).

Instance 2 (port 5297) log:
  (nothing at all for quote-created:40)
```
```
AuditLogs:          exactly 1 row for QuoteId=40
ProcessedMessages:  exactly 1 row for MessageId=quote-created:40
```
Instance 2 didn't log an abandoned attempt or a duplicate-skip - it never
received the message at all. This is genuine competition (Service Bus's
per-message lock granted to exactly one receiver), not two deliveries
that happened to get deduped down to one write.

### Process restart mid-flight - unsettled message redelivered, then deduped

The hardest case to reproduce reliably, so handled deliberately: a
**temporary** 15-second delay was added between the transaction commit
and `CompleteMessageAsync` in `AuditSubscriptionWorker` (reverted
immediately after this test, confirmed by rebuilding and rerunning the
full suite again afterward - same 68/39-plus-1 as above). Ran the app
from its built DLL directly (not `dotnet run`, so a signal reaches the
real process, same lesson already learned in Day 18), published a
message for quote 42, and sent `kill -9` the instant the "committed" log
line appeared - well inside the 15-second window, confirmed no graceful
"Application is shutting down" line in the log:

```
12:47:40  Audit write committed for quote 42 (MessageId=quote-created:42).
          <- kill -9 sent here, milliseconds later
          <- process confirmed dead, no shutdown log line
```
```
AuditLogs row already present at this point: Id=9, QuoteId=42, CreatedByUserId=8
```
The transaction had committed; the message was never completed, so it
stayed locked to a now-dead process. Waited out the 30-second
`LockDuration` from `Config.json`, then started a **fresh** instance (no
artificial delay this time - normal code path):

```
12:48:40 [INF] Duplicate delivery of quote-created:42 on audit (DeliveryCount=2) - skipped, already processed.
```
`DeliveryCount=2` confirms this is the same message, redelivered after
lock expiry, not a new one. `AuditLogs` still has **exactly one** row for
quote 42 (`Id=9`, the original) - the redelivery was correctly deduped,
not double-written. This is the precise scenario the settlement ordering
above exists to make safe.

## What could not be verified

- **A genuinely simultaneous race** between two competing consumers on
  the *exact* same message (both receiving it at the literal same
  instant) wasn't reproduced - Service Bus's own locking makes that hard
  to force deliberately, and the competing-consumers test above already
  demonstrates the real mechanism (only one receiver ever sees a given
  message under normal operation). The unique index's role as a backstop
  for a true race is asserted from how the constraint works, not from
  having actually triggered a duplicate-commit attempt and watched it
  fail.
- **The 39-passing integration tests' actual portability** - confirmed
  they pass in *this* environment, with *my* local user-secrets already
  set. Not verified on a clean machine, and per the explanation above,
  they would not pass there without the same secrets - this is a real gap
  in the test project, out of this task's scope to fix.
- **Nothing was deployed.** All of this ran against the local emulator
  only; the live Azure App Service (`day-17`/`day-18`'s deployment) still
  runs Day 18's in-memory queue, untouched, per the constraint not to
  touch deployed configuration.

## Day 18 vs. Day 19 - DRAFT, for you to rewrite

**Marked as a draft. This is a first pass at naming what actually
changed and why, not a final comparison.**

What Day 19 actually bought, demonstrated concretely above rather than
asserted:

- **Persistence.** Day 18's queue was process memory - an App Service
  restart (which, per `day-17/README.md`, happens routinely on the F1
  tier this app runs on) silently loses anything queued. Service Bus
  persists the message until it's explicitly settled; the mid-flight
  restart test above is the direct proof - the message survived a hard
  process kill and came back.
- **Competing consumers.** Day 18's queue lived inside one process; there
  was no meaningful way to run two instances against it without each
  having its own separate, disconnected queue. Service Bus made "two
  full app instances, one shared subscription, message handled exactly
  once" something that just works, verified above.
- **Dead-lettering.** Day 18 had no equivalent at all - a throwing item
  was logged and discarded, permanently, with nothing left to inspect
  afterward. Service Bus's DLQ keeps the poison message, with a reason
  and a body, somewhere a human can actually go look.

What Day 19 cost, also worth naming plainly:

- A new external dependency (the emulator locally, a real namespace in
  Azure) that Day 18 didn't need - nothing to run, nothing to configure,
  nothing that could fail to connect.
- A new NuGet package and a meaningfully larger surface area:
  `ProcessedMessage`, transaction-then-settle ordering, `Poison`-sentinel
  handling - none of which existed in Day 18's ~120-line queue+worker.
- The integration-test gap documented above is a direct cost of this
  change, not incidental - Day 18 needed nothing at test-host startup;
  Day 19 needs a reachable broker just to boot the app at all.

**Where the line should sit** (draft, rewrite this part especially): Day
18's in-memory queue is still the right call for a job where losing it
occasionally is genuinely fine and there's only ever one instance running
- which was true of the audit job *as a demo feature* on a single F1
instance. What actually changed to justify Day 19 isn't that the job got
more important; it's that this exercise asked for competing consumers and
a DLQ specifically, which Day 18's design has no way to provide at any
price - not "Service Bus is better," but "the requirement changed to
something a single in-process queue cannot do, full stop, regardless of
how well-written it is."

## Phase 1 — build

*(In progress — this section is being filled in as each piece is built
and verified live, not written in advance.)*

**Environment note, for anyone reading this later:** the Docker image
pulls for the emulator and its SQL Edge dependency were unusually slow in
the sandbox this was built in (roughly 660MB of `azure-sql-edge` at an
observed ~166 KB/s average) — over an hour for both images to land. That
delay is specific to this environment's network conditions, not a
property of the emulator itself; on a normal connection this is a
five-minute `docker compose up`.
