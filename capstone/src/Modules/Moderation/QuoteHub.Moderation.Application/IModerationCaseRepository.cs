using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Application;

public interface IModerationCaseRepository
{
    void Add(ModerationCase moderationCase);

    Task<ModerationCase?> GetByIdAsync(int id, CancellationToken ct);

    // Backing the "list open cases" endpoint - open meaning
    // ModerationCaseStatus.Pending, not yet decided.
    Task<IReadOnlyList<ModerationCase>> GetOpenAsync(CancellationToken ct);
}
