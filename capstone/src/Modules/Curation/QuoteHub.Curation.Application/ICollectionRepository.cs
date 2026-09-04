using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Application;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken ct);

    // Backing the moderation-decision fan-out: every collection currently
    // holding this quote needs its read-model copy updated. See
    // QuoteModerationDecidedHandler.
    Task<IReadOnlyList<Collection>> GetContainingQuoteAsync(int quoteId, CancellationToken ct);

    void Add(Collection collection);
}
