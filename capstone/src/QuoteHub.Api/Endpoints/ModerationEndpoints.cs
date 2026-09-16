using QuoteHub.Moderation.Infrastructure;

namespace QuoteHub.Api.Endpoints;

// Auth-free for now, same reasoning as CurationEndpoints.cs: a Result
// failure is always a 400 (ModerationCase returns Result on invariant
// violations by design), a missing case is a 404.
public static class ModerationEndpoints
{
    public static void MapModerationEndpoints(this WebApplication app)
    {
        var cases = app.MapGroup("/moderation/cases");

        cases.MapPost("", async (ReportQuoteRequest request, IModerationService service, CancellationToken ct) =>
        {
            if (request.QuoteId <= 0)
                return Results.BadRequest(new { error = "QuoteId is required." });
            if (request.ReportedByUserId <= 0)
                return Results.BadRequest(new { error = "ReportedByUserId is required." });

            var result = await service.ReportAsync(request, ct);
            return result.IsSuccess
                ? Results.Created($"/moderation/cases/{result.Value.Id}", result.Value)
                : Results.BadRequest(new { error = result.Error });
        });

        cases.MapGet("", async (IModerationService service, CancellationToken ct) =>
            Results.Ok(await service.GetOpenCasesAsync(ct)));

        cases.MapPost("/{id:int}/decide", async (int id, DecideCaseRequest request, IModerationService service, CancellationToken ct) =>
        {
            var result = await service.DecideAsync(id, request, ct);
            if (result is null)
                return Results.NotFound();

            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
        });
    }
}
