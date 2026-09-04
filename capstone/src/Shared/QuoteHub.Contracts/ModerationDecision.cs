namespace QuoteHub.Contracts;

// Part of the shared vocabulary, not owned by either module's Domain -
// Moderation decides it, Curation reacts to it, and neither should have
// to reference the other's internal enum to agree on what it means.
public enum ModerationDecision
{
    Hidden,
    Restored,
}
