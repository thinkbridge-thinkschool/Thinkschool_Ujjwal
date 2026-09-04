namespace QuoteHub.Moderation.Domain;

// Moderation's own vocabulary for what a case was decided as. Distinct
// from QuoteHub.Contracts.ModerationDecision, which is the event
// vocabulary published to other modules once this is decided.
public enum ModerationOutcome
{
    Hidden,
    Restored,
}
