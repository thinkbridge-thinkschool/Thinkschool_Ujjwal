# QuoteHub — Design

## Bounded contexts

Four contexts exist in the product; two are built here.

- **Curation** (built) — owns `Collection`, a user-curated ordered set of quotes. Its boundary sits at "what does a reader see when they open a collection," which is deliberately *not* the same question as "is this quote currently visible." Curation answers the first question from its own read-model copy (below); it never answers the second by asking Moderation live.
- **Moderation** (built) — owns the lifecycle of a report: `ModerationCase`, opened on `QuoteReported`, closed by a moderator's decision. Its boundary is "should this quote render," which it publishes, it doesn't enforce elsewhere.
- **Authoring** (named, out of scope) — would own quote/author authorship and the canonical quote text. Curation's read-model copy is a copy *of* Authoring's data, not Authoring itself.
- **Discovery** (named, out of scope) — would own search/browse/ranking. Not touched.

Authoring and Discovery are absent from `src/` entirely — no stub projects, no placeholder interfaces. Scaffolding a context nobody is implementing yet just gives the architecture tests something meaningless to guard.

## Core aggregate: `Collection`

`QuoteHub.Curation.Domain.Collection` — construction only via `Collection.Create(name, ownerId, ownerUserId?)`, returning `Result<Collection>`; every mutator returns `Result`. No exceptions for invariant violations, by design (see below).

Invariants:
- Name: 1–80 characters, required.
- At most `MaxSlots` (50) **slots** — see tombstones below for why this is slots, not visible items.
- No duplicate `QuoteId` within a collection.
- `RemoveItem` on a non-existent `QuoteId` fails.

**Result, not exceptions.** An existing sibling app in this repo (`day-5/QuotesApi`'s `Collection`) throws on every invariant violation. This aggregate diverges deliberately: a moderation-decision handler processing hundreds of collections per event needs to keep going past one that's in a bad state, and "did this succeed" as a return value is cheaper to route through a fan-out loop than try/catch per iteration. The structural style — private setters, EF-only parameterless constructor, backing list exposed as read-only — is kept from the original; the error-reporting mechanism is not.

**Tombstones.** A moderation decision must never change a collection's item *count*. When a quote is hidden, the matching `CollectionItem` stays in `Items` — its slot, its `AddedAt`, its position — but drops out of `VisibleItems`. `TotalSlots` (all items) and `VisibleItems` (rendered items) are distinct on purpose:
- Removal is owner-driven and destructive on purpose — the owner asked for it. A moderation hide is neither: the owner didn't ask, and it may be reversed on appeal. If a hide silently shrank the collection the way a removal does, an appeal that restores the quote would have nowhere to restore it *to* — the owner's curated ordering around that slot is already gone.
- A collection silently losing an item is worse than a collection showing "1 item unavailable." The reader can tell something happened; with silent removal they can't tell whether the owner reconsidered or the platform intervened.
- The cost is explicit, not hidden: a hidden slot still counts toward the 50-item cap. An owner near the cap can find themselves unable to add a new quote because of items they can no longer even see. That's the trade this scaffold makes — not a bug to fix later, a bound this design accepts now.

## The boundary decision: read-model copy, not live calls

Each `CollectionItem` carries its own copy of `QuoteId`, `AuthorName`, `TextSnippet`, and `Visibility`. Rendering a collection reads only from Curation's own storage — it never calls Moderation (or Authoring) per item. The alternative — fetch-on-render — reintroduces an N+1 at the *module* boundary instead of the database boundary: a 50-item collection would mean 50 cross-module lookups just to paint a page.

The copy is kept current by reacting to `QuoteModerationDecided` (`Collection.ApplyModerationDecision`), not by querying live. That means the copy can be stale between the moment Moderation commits a decision and the moment the event is applied here. The staleness is asymmetric and that asymmetry is the point: a quote that was just hidden may still render for a short window (until its event lands), but a quote that's actually fine is never wrongly hidden by this lag. For a quotes app, a few seconds of a since-hidden quote still showing is an acceptable cost, not an incident. That calculus flips for content where a stale "still visible" is unacceptable (medical, legal, safety-critical) — there, query live and eat the latency.

The consequence is stated plainly, not glossed over: two modules now hold a copy of quote text, and they can diverge. That makes the event flow below load-bearing infrastructure, not a nice-to-have — if it silently stops, the divergence has no ceiling.

## Async flows

Both flows are async for a stated reason, and both use the transactional outbox pattern (each module's own `OutboxMessage` table, in its own schema) rather than publishing inline: committing the aggregate change without committing the event in the same transaction is exactly the failure mode this pattern exists to remove — a decision or a report that "succeeded" but never told anyone. This scaffold defines the outbox tables and the event contracts and stops there; the relay that polls, dispatches, and marks rows processed is not reimplemented here — see `day-20`'s `OutboxRelay` for the intended shape, referenced rather than duplicated.

1. **Moderation decision → collection items updated** (`QuoteModerationDecided`, handled by `QuoteModerationDecidedHandler` in Curation.Application). Async because one quote can sit in hundreds of collections; a moderator's decision must not block on a fan-out write across all of them.
2. **Quote reported → moderation queue entry** (`QuoteReported`, handled by `QuoteReportedHandler` in Moderation.Application). Async because reporting is a reader-facing action that must stay fast, and must still succeed even if Moderation is degraded or offline — the report has to land regardless of what's listening.

## Why a modular monolith, not microservices

One team, one product, one deploy cadence today. Splitting Curation and Moderation into separate services now buys network calls and partial-failure handling for a boundary that doesn't yet need independent scaling, independent deploys, or a second team owning it — the cost is paid immediately, the benefit is speculative. A modular monolith gets the boundary that actually matters here — the *dependency* boundary, so Curation and Moderation can't quietly reach into each other's internals — without paying for the *deployment* boundary until something concrete demands it (a team split, a load profile that diverges enough to need independent scaling). The boundary is enforced the same way either way: `QuoteHub.ArchitectureTests` fails if a project reference crosses it, regardless of whether the two sides ship together or separately. If that day comes, the module split (own Domain/Application/Infrastructure, own schema, communication only through `Contracts` and integration events) is what a service extraction would start from — the seams are already there.

One database, one schema per module (`curation.*`, `moderation.*`). No cross-schema foreign keys, no cross-schema joins — the schema boundary mirrors the module boundary so nobody can accidentally query around `Contracts` from SQL.

## What we'd revisit

*(Draft — the author is rewriting this section in their own words.)*

Two calls here are the ones most likely to be wrong in six months:

- **The tombstone.** Counting hidden slots against the 50-item cap is a real, felt cost for an owner with a heavily-moderated collection. Worth watching whether that turns out to be the right trade once there's real moderation volume, or whether tombstones need their own budget separate from live items.
- **The copied read model.** Two copies of quote text that can diverge is a real ongoing liability, not a one-time cost. Worth watching whether the outbox/event path stays reliable enough in practice to keep that divergence bounded, or whether some collections (or some fields) end up needing a live read after all.
