# ADR 0001: Curation reads its own eventually-consistent copy of moderation state, instead of calling Moderation live

## Status

Accepted (Day 17 design; revisited and confirmed Day 28).

## Context

`Collection` (Curation) renders a reader-facing list of quotes. Whether any one of those quotes should currently be visible is a fact owned by a different bounded context — Moderation — not by Curation. Every render of a collection therefore needs an answer to a question Curation doesn't own the source of truth for: "is this quote still visible?"

Two module boundaries meet here, and how they meet is the decision this ADR is about. It's the single highest-blast-radius call in the whole design: it's exercised on every read, it determines whether Curation and Moderation can ever disagree about the world, and getting it wrong either reintroduces the coupling the module split exists to prevent, or produces silent, unbounded data divergence.

## Decision

Curation keeps its own denormalized copy of the fields it needs to render (`QuoteId`, `AuthorName`, `TextSnippet`, `Visibility`) on each `CollectionItem`, written once when the item is added and updated only by reacting to `QuoteModerationDecided` — an integration event Moderation publishes through its own transactional outbox, never by Curation querying Moderation synchronously.

Rendering a collection is therefore always a single read against Curation's own storage. No render, however large the collection, issues a cross-module call.

## Alternatives considered

1. **Live query per item at render time** (Curation calls into Moderation, or a shared "is this visible" service, once per `CollectionItem` while painting a page).
   Rejected: this reintroduces an N+1, just moved from the database boundary to the module boundary. A 50-item collection (the aggregate's own cap) means up to 50 synchronous cross-module calls to render one page. It also means Curation's read latency now depends on Moderation's availability for a question Curation didn't need Moderation to answer *live* — a reader viewing an already-published collection shouldn't fail because the moderation service is briefly down.

2. **One batched live query per render** (Curation sends Moderation the full list of `QuoteId`s on a collection in one call, gets visibility back for all of them).
   Rejected as the primary path, though it's the strongest alternative of the three: it removes the N+1 but keeps the availability coupling — Curation can no longer render *anything* if Moderation is down, which is a strictly worse failure mode than "a moderation decision takes a few seconds to reflect," given how rarely visibility actually changes relative to how often a collection is read.

3. **Shared database view / cross-schema join** (Curation's read query joins directly against Moderation's schema for current visibility).
   Rejected outright: it would work today and quietly destroy the entire boundary. `curation.*` and `moderation.*` are separate schemas specifically so nobody can query around the module boundary from SQL (enforced structurally, and by `QuoteHub.ArchitectureTests` at the project-reference level). A cross-schema join has the same effect as a project reference the architecture tests would fail on — it's a load-bearing invariant with a hole cut in it.

4. **Chosen: async, event-driven, eventually-consistent local copy** — described above.

## Consequences

**Accepted trade-off — the staleness window, and why it's asymmetric on purpose.** Between the moment Moderation commits a hide/restore decision and the moment `QuoteModerationDecidedHandler` applies it here, Curation's copy can be behind the truth. That staleness only ever runs in one direction: a quote that was *just* hidden may still render for a short window; a quote that's actually fine is never wrongly hidden by the lag, because hiding requires the event to arrive, and nothing here ever hides speculatively. For a quotes app, a few seconds of a since-hidden quote still showing is an acceptable, bounded cost — not an incident. That calculus does not generalize: for content where a stale "still visible" is itself the harm (medical, legal, safety-critical material), the batched-live-query alternative above, eating its availability coupling, would be the right call instead. This system is not that system.

**Accepted trade-off — two copies that can diverge.** Curation now holds a second copy of quote text and visibility that Authoring/Moderation also hold. That makes the event pipeline (outbox → relay → handler) load-bearing infrastructure: if it silently stops, the two copies drift with no ceiling and no built-in alarm. This ADR does not resolve that risk, it names it as the cost being paid. `day-20`'s `OutboxRelay` is the reference shape for the piece that closes this loop; it is deliberately not reimplemented in this scaffold (see "Day-by-day build plan" below) — until it exists here, this decision is designed but not yet load-bearing in a running system.

**Gap this ADR does not close: the staleness window is asserted, not measured.** "A few seconds" is a claim about `OutboxRelay`'s expected poll interval, not a bound this design enforces or watches. Nothing here would surface it if lag grew to minutes — or stopped advancing entirely because the relay died — until a reader or a moderator noticed a decision that "didn't take." That's a real hole in an otherwise-deliberate trade-off: the asymmetry argument above only holds if the window stays short, and nothing here confirms it does. See the build plan's Day 4 for the minimum fix (an age-of-oldest-unprocessed-row metric, alertable), which this ADR now depends on rather than treating as optional polish.

**What would overturn this decision.** If collections routinely needed to reflect a moderation decision within sub-second latency (not true today — nothing about a quotes app has that requirement), or if the event pipeline's reliability in practice turns out worse than assumed here, alternative 2 (batched live query, availability-coupled but always-current) is the fallback this ADR would be revisited in favor of. That's a call to make from operating this system, not from designing it — noted here so it isn't rediscovered from scratch later.
