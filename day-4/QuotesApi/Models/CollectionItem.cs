namespace QuotesApi.Models;

public class CollectionItem
{
    public int QuoteId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    // Required by EF Core for materialization. Coverage tools may not attribute
    // hits to constructors EF Core invokes via reflection, so 0 visits here
    // doesn't prove it's unused — do not remove based on coverage alone.
    private CollectionItem() { }

    public CollectionItem(int quoteId, DateTimeOffset addedAt)
    {
        QuoteId = quoteId;
        AddedAt = addedAt;
    }
}