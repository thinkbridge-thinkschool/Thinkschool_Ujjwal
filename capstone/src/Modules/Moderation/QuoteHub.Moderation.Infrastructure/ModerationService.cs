using QuoteHub.Moderation.Application;
using QuoteHub.Moderation.Domain;
using QuoteHub.SharedKernel;

namespace QuoteHub.Moderation.Infrastructure;

public sealed class ModerationService : IModerationService
{
    private readonly IModerationCaseRepository _repository;
    private readonly ModerationDbContext _dbContext;

    public ModerationService(IModerationCaseRepository repository, ModerationDbContext dbContext)
    {
        _repository = repository;
        _dbContext = dbContext;
    }

    public async Task<Result<ModerationCaseResponse>> ReportAsync(ReportQuoteRequest request, CancellationToken ct)
    {
        var created = ModerationCase.Create(request.QuoteId, request.ReportedByUserId, request.Reason, DateTimeOffset.UtcNow);
        if (created.IsFailure)
            return Result<ModerationCaseResponse>.Failure(created.Error);

        _repository.Add(created.Value);
        await _dbContext.SaveChangesAsync(ct);

        return Result<ModerationCaseResponse>.Success(Map(created.Value));
    }

    public async Task<IReadOnlyList<ModerationCaseResponse>> GetOpenCasesAsync(CancellationToken ct)
    {
        var open = await _repository.GetOpenAsync(ct);
        return open.Select(Map).ToList();
    }

    public async Task<Result<ModerationCaseResponse>?> DecideAsync(int caseId, DecideCaseRequest request, CancellationToken ct)
    {
        var moderationCase = await _repository.GetByIdAsync(caseId, ct);
        if (moderationCase is null)
            return null;

        var result = moderationCase.Decide(request.Outcome, DateTimeOffset.UtcNow);
        if (result.IsFailure)
            return Result<ModerationCaseResponse>.Failure(result.Error);

        await _dbContext.SaveChangesAsync(ct);
        return Result<ModerationCaseResponse>.Success(Map(moderationCase));
    }

    private static ModerationCaseResponse Map(ModerationCase moderationCase) => new(
        moderationCase.Id,
        moderationCase.QuoteId,
        moderationCase.ReportedByUserId,
        moderationCase.Reason,
        moderationCase.ReportedAt,
        moderationCase.Status,
        moderationCase.Outcome,
        moderationCase.DecidedAt);
}
