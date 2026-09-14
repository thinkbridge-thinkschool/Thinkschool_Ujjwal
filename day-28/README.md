# Day 28 — Design review + ADR

No external mentor or peer was available in this session to actually critique the design — that's stated plainly rather than faked. What follows is a rigorous self-review of `capstone/DESIGN.md` instead, done the way a mentor pass would be done (find the sharpest real objection, then change the design in response to it, not just note it) — and it's labeled as a self-review throughout, not dressed up as someone else's.

## The ADR

[`capstone/docs/adr/0001-eventually-consistent-read-model.md`](../capstone/docs/adr/0001-eventually-consistent-read-model.md)

**The decision:** Curation renders collections from its own local, denormalized copy of quote text/visibility, kept current by reacting to Moderation's `QuoteModerationDecided` integration event — never by querying Moderation live, per item or batched.

This is the one decision in the design that matters most, for three concrete reasons, not just because `DESIGN.md` gives it its own section:
- It's exercised on **every single read** of a collection, not on some rare path — get this wrong and it's wrong constantly, not occasionally.
- It's **the actual module boundary** — Curation and Moderation are two bounded contexts specifically because a decision like this exists to be made; a live-query answer would have quietly dissolved the boundary the rest of the design (schema-per-module, `QuoteHub.ArchitectureTests`, no cross-project references) exists to enforce.
- It's the source of the design's one real, ongoing liability — two copies of quote data that can diverge — which the ADR names as a cost being paid, not a problem solved.

**Alternatives weighed** (full reasoning in the ADR): a live query per item (rejected — reintroduces N+1 at the module boundary instead of the DB boundary, up to 50 cross-module calls per page); one batched live query per render (the strongest rejected alternative — removes the N+1 but couples Curation's *entire* render path to Moderation's availability, for a visibility change that's rare relative to how often a collection is read); a shared-schema join (rejected outright — it would work and quietly destroy the boundary the schemas exist to protect).

## Top critique, and how it changed the design

**The critique:** the ADR's central argument — that a few seconds of staleness is an acceptable, asymmetric cost — is an *assertion*, not something the design measures or watches. If `OutboxRelay`'s lag grew from seconds to minutes, or the relay died outright, nothing in the current design would surface that until a reader or a moderator noticed a decision that silently "didn't take." The asymmetry argument (a just-hidden quote may show briefly; a fine quote is never wrongly hidden) only holds while the window stays short — and nothing confirms it does.

**How it changed the design:** this wasn't left as a noted-but-unaddressed gap. The ADR now states plainly that it *depends on* an oldest-unprocessed-outbox-row-age metric existing and being alertable — not as future polish, but as the thing that makes the accepted trade-off actually true in a running system rather than just true on paper. That requirement is now Day 4 of the build plan below, ahead of any new feature work, because the ADR's own trade-off isn't real without it.

## Day-by-day build plan

Grounded in what's actually in the repo today, checked directly rather than assumed: `SharedKernel`, `Contracts`, both modules' `Domain` (`Collection`, `ModerationCase`) and `Infrastructure` (EF configs, repositories, outbox tables, SQL Server wired) exist and compile. 17 domain tests + 9 architecture-boundary tests pass. What's genuinely missing: any HTTP endpoint beyond `/health`, the outbox relay itself, EF migrations (none generated yet), a `Moderation.Tests` project (doesn't exist — only Curation has domain tests), any application-layer or integration test coverage, and auth (none wired in `QuoteHub.Api` today).

1. **Migrations + a real database round-trip.** Generate the first EF migration for both `curation.*` and `moderation.*` schemas; prove `Collection.Create` → save → reload survives a real SQL Server instance, not just an in-memory context. Nothing after this point is trustworthy until persistence itself is proven.
2. **Curation endpoints**, thin and auth-free for now: create collection, add item, remove item, get collection (rendering `VisibleItems`, per the tombstone rule). Reuses `day-5/QuotesApi`'s endpoint shape and validation patterns where they transfer directly; does not reuse its exception-for-invariant-violations style — `Collection` here returns `Result` on purpose (see `DESIGN.md`), and the endpoints must translate `Result` failures to `400`s, not let an exception escape.
3. **Moderation endpoints + `Moderation.Tests`.** Report a quote, list open cases, decide a case. The missing test project gets created here, not skipped — `ModerationCase` currently has zero test coverage, which is a real gap independent of anything else on this list.
4. **`OutboxRelay` + the lag metric the critique above requires.** Adapted from `day-20`'s reference shape, one instance per module (each polling its own schema's outbox table). The age-of-oldest-unprocessed-row gauge ships in the same day as the relay itself, not after — per the ADR update above, the relay isn't "done" without it.
5. **End-to-end integration test**: report a quote → decide "hidden" → poll until the event lands → assert the collection's `VisibleItems` actually dropped it, `TotalSlots` did not. This is the one test that actually exercises ADR 0001's trade-off, not just each module in isolation.
6. **Auth + wiring into the existing identity model.** Bearer-token auth matching `day-5/QuotesApi`'s scheme (same Key Vault-backed signing key, same claims shape) so a real user identity flows through `CreatedByUserId`/ownership checks on `Collection`, rather than inventing a second auth system for the capstone.

Day 4 and Day 5 are the two days this design review actually changed, versus what a first pass at the same list would have produced without the critique above.
