using QuoteHub.SharedKernel;

namespace QuoteHub.Curation.Infrastructure;

// The one surface QuoteHub.Api is allowed to call for Curation commands -
// see CollectionContracts.cs for why this lives here instead of
// Application. A null Result (AddItemAsync/RemoveItemAsync) means "no
// collection with that id"; a non-null failed Result means the domain
// rejected the operation - callers map the two to 404 and 400
// respectively.
public interface ICollectionService
{
    Task<Result<CollectionResponse>> CreateAsync(CreateCollectionRequest request, CancellationToken ct);

    Task<Result<CollectionResponse>?> AddItemAsync(int collectionId, AddCollectionItemRequest request, CancellationToken ct);

    Task<Result?> RemoveItemAsync(int collectionId, int quoteId, CancellationToken ct);

    Task<CollectionResponse?> GetAsync(int collectionId, CancellationToken ct);
}
