using System.Text.Json.Serialization;
using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Infrastructure;

// Request/response shapes for QuoteHub.Api's /moderation/cases endpoints.
// Same reasoning as Curation.Infrastructure/CollectionContracts.cs: Api
// may only declare a reference to a module's Infrastructure project, so
// the web-facing contract lives here rather than in Moderation.Application
// or Moderation.Domain.

public sealed record ReportQuoteRequest(int QuoteId, int ReportedByUserId, string Reason);

public sealed record DecideCaseRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationOutcome Outcome);

public sealed record ModerationCaseResponse(
    int Id,
    int QuoteId,
    int ReportedByUserId,
    string Reason,
    DateTimeOffset ReportedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationCaseStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationOutcome? Outcome,
    DateTimeOffset? DecidedAt);
