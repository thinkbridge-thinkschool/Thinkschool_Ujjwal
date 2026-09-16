using QuoteHub.SharedKernel;

namespace QuoteHub.Moderation.Infrastructure;

// The one surface QuoteHub.Api is allowed to call for Moderation commands
// - see ModerationContracts.cs for why this lives here instead of
// Application. A null Result from DecideAsync means "no case with that
// id"; a non-null failed Result means the domain rejected the operation
// (already decided) - callers map the two to 404 and 400 respectively.
public interface IModerationService
{
    Task<Result<ModerationCaseResponse>> ReportAsync(ReportQuoteRequest request, CancellationToken ct);

    Task<IReadOnlyList<ModerationCaseResponse>> GetOpenCasesAsync(CancellationToken ct);

    Task<Result<ModerationCaseResponse>?> DecideAsync(int caseId, DecideCaseRequest request, CancellationToken ct);
}
