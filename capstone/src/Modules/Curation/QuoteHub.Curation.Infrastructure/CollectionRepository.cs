using Microsoft.EntityFrameworkCore;
using QuoteHub.Curation.Application;
using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Infrastructure;

public sealed class CollectionRepository : ICollectionRepository
{
    private readonly CurationDbContext _dbContext;

    public CollectionRepository(CurationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Collection?> GetByIdAsync(int id, CancellationToken ct) =>
        _dbContext.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Collection>> GetContainingQuoteAsync(int quoteId, CancellationToken ct)
    {
        // The owned CollectionItem collection doesn't translate to a
        // clean server-side "any item has this QuoteId" query, so this
        // scaffold loads and filters in memory - the honest answer for a
        // handler stub, not a claim this scales past a handful of
        // affected collections per decision. Revisit before this ever
        // sees real traffic.
        var all = await _dbContext.Collections.ToListAsync(ct);
        return all.Where(c => c.Items.Any(i => i.QuoteId == quoteId)).ToList();
    }

    public void Add(Collection collection) => _dbContext.Collections.Add(collection);
}
