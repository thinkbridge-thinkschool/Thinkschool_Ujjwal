using QuoteHub.SharedKernel;

namespace QuoteHub.Curation.Domain;

// A slot in a Collection, keyed by QuoteId. Carries a read-model copy of
// the quote (AuthorName, TextSnippet, Visibility) rather than a reference
// to a live Moderation/Authoring record - see DESIGN.md's boundary
// decision. The copy can drift from the source until the next
// QuoteModerationDecided is applied; that staleness is accepted, not a
// bug.
public sealed class CollectionItem : Entity<int>
{
    public int QuoteId => Id;
    public string AuthorName { get; private set; }
    public string TextSnippet { get; private set; }
    public QuoteVisibility Visibility { get; private set; }
    public DateTimeOffset AddedAt { get; }

    private CollectionItem() // EF
    {
        AuthorName = string.Empty;
        TextSnippet = string.Empty;
    }

    internal CollectionItem(int quoteId, string authorName, string textSnippet, QuoteVisibility visibility, DateTimeOffset addedAt)
        : base(quoteId)
    {
        AuthorName = authorName;
        TextSnippet = textSnippet;
        Visibility = visibility;
        AddedAt = addedAt;
    }

    // Called only by Collection.ApplyModerationDecision - flips whether
    // this slot renders without ever removing it. That's the tombstone.
    internal void ApplyVisibility(QuoteVisibility visibility) => Visibility = visibility;
}
