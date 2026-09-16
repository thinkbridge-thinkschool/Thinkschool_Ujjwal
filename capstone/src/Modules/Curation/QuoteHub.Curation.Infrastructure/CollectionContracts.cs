using System.Text.Json.Serialization;
using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Infrastructure;

// Request/response shapes for QuoteHub.Api's /collections endpoints. Live
// here, not in QuoteHub.Curation.Application, because QuoteHub.Api is only
// allowed to declare a reference to a module's Infrastructure project
// (QuoteHub.ArchitectureTests.DependencyRuleTests.Api_references_module_infrastructure_only)
// - putting the web-facing contract anywhere else would mean either the
// Api project reaching directly into Domain/Application (the thing that
// rule exists to prevent) or duplicating these types on the Api side.

public sealed record CreateCollectionRequest(string Name, int OwnerId, string? OwnerUserId);

public sealed record AddCollectionItemRequest(
    int QuoteId,
    string AuthorName,
    string TextSnippet,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] QuoteVisibility Visibility);

public sealed record CollectionItemResponse(int QuoteId, string AuthorName, string TextSnippet, DateTimeOffset AddedAt);

public sealed record CollectionResponse(
    int Id,
    string Name,
    int OwnerId,
    string? OwnerUserId,
    int TotalSlots,
    IReadOnlyList<CollectionItemResponse> Items);
