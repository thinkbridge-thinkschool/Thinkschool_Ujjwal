using QuoteHub.Curation.Application;
using QuoteHub.Curation.Domain;
using QuoteHub.SharedKernel;

namespace QuoteHub.Curation.Infrastructure;

public sealed class CollectionService : ICollectionService
{
    private readonly ICollectionRepository _repository;
    private readonly CurationDbContext _dbContext;

    public CollectionService(ICollectionRepository repository, CurationDbContext dbContext)
    {
        _repository = repository;
        _dbContext = dbContext;
    }

    public async Task<Result<CollectionResponse>> CreateAsync(CreateCollectionRequest request, CancellationToken ct)
    {
        var created = Collection.Create(request.Name, request.OwnerId, request.OwnerUserId);
        if (created.IsFailure)
            return Result<CollectionResponse>.Failure(created.Error);

        _repository.Add(created.Value);
        await _dbContext.SaveChangesAsync(ct);

        return Result<CollectionResponse>.Success(Map(created.Value));
    }

    public async Task<Result<CollectionResponse>?> AddItemAsync(int collectionId, AddCollectionItemRequest request, CancellationToken ct)
    {
        var collection = await _repository.GetByIdAsync(collectionId, ct);
        if (collection is null)
            return null;

        var result = collection.AddItem(request.QuoteId, request.AuthorName, request.TextSnippet, request.Visibility, DateTimeOffset.UtcNow);
        if (result.IsFailure)
            return Result<CollectionResponse>.Failure(result.Error);

        await _dbContext.SaveChangesAsync(ct);
        return Result<CollectionResponse>.Success(Map(collection));
    }

    public async Task<Result?> RemoveItemAsync(int collectionId, int quoteId, CancellationToken ct)
    {
        var collection = await _repository.GetByIdAsync(collectionId, ct);
        if (collection is null)
            return null;

        var result = collection.RemoveItem(quoteId);
        if (result.IsFailure)
            return result;

        await _dbContext.SaveChangesAsync(ct);
        return result;
    }

    public async Task<CollectionResponse?> GetAsync(int collectionId, CancellationToken ct)
    {
        var collection = await _repository.GetByIdAsync(collectionId, ct);
        return collection is null ? null : Map(collection);
    }

    // VisibleItems, not Items - per the tombstone rule, a reader never
    // sees a hidden slot even though it still counts toward TotalSlots.
    private static CollectionResponse Map(Collection collection) => new(
        collection.Id,
        collection.Name,
        collection.OwnerId,
        collection.OwnerUserId,
        collection.TotalSlots,
        collection.VisibleItems
            .Select(i => new CollectionItemResponse(i.QuoteId, i.AuthorName, i.TextSnippet, i.AddedAt))
            .ToList());
}
