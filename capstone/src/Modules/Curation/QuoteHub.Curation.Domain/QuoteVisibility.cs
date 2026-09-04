namespace QuoteHub.Curation.Domain;

// Curation's own copy of Moderation's decision, carried on each
// CollectionItem's read-model copy. Not the same enum as Moderation's
// ModerationDecision (QuoteHub.Contracts) - that's the event vocabulary;
// this is what a rendered item actually is right now.
public enum QuoteVisibility
{
    Visible,
    Hidden,
}
