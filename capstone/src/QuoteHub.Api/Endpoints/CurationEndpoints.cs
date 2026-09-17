using QuoteHub.Curation.Infrastructure;

namespace QuoteHub.Api.Endpoints;

// Auth-free for now (see day-28's build plan - auth is a later day); every
// handler here does the id-shaped sanity checks a request body's own
// primitive fields need (mirrors day-5/QuotesApi's EndpointExtensions.cs),
// then hands off to ICollectionService and translates its Result back to
// an HTTP status. A Result failure is always a 400 - Collection returns
// Result on invariant violations by design (see DESIGN.md), and an
// endpoint letting that surface as anything but a 400 would quietly undo
// that choice. A missing collection (service returns null) is the only
// thing that becomes a 404 here.
public static class CurationEndpoints
{
    public static void MapCurationEndpoints(this WebApplication app)
    {
        var collections = app.MapGroup("/collections");

        collections.MapPost("", async (CreateCollectionRequest request, ICollectionService service, CancellationToken ct) =>
        {
            if (request.OwnerId <= 0)
                return Results.BadRequest(new { error = "OwnerId is required." });

            var result = await service.CreateAsync(request, ct);
            return result.IsSuccess
                ? Results.Created($"/collections/{result.Value.Id}", result.Value)
                : Results.BadRequest(new { error = result.Error });
        });

        collections.MapGet("/{id:int}", async (int id, ICollectionService service, CancellationToken ct) =>
        {
            var collection = await service.GetAsync(id, ct);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        });

        collections.MapPost("/{id:int}/items", async (int id, AddCollectionItemRequest request, ICollectionService service, CancellationToken ct) =>
        {
            if (request.QuoteId <= 0)
                return Results.BadRequest(new { error = "QuoteId is required." });
            if (string.IsNullOrWhiteSpace(request.AuthorName))
                return Results.BadRequest(new { error = "AuthorName is required." });
            if (string.IsNullOrWhiteSpace(request.TextSnippet))
                return Results.BadRequest(new { error = "TextSnippet is required." });

            var result = await service.AddItemAsync(id, request, ct);
            if (result is null)
                return Results.NotFound();

            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
        });

        collections.MapDelete("/{id:int}/items/{quoteId:int}", async (int id, int quoteId, ICollectionService service, CancellationToken ct) =>
        {
            var result = await service.RemoveItemAsync(id, quoteId, ct);
            if (result is null)
                return Results.NotFound();

            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
        });
    }
}
