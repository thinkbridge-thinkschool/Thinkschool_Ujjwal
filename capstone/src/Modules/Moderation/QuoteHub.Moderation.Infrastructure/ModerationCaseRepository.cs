using Microsoft.EntityFrameworkCore;
using QuoteHub.Moderation.Application;
using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Infrastructure;

public sealed class ModerationCaseRepository : IModerationCaseRepository
{
    private readonly ModerationDbContext _dbContext;

    public ModerationCaseRepository(ModerationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(ModerationCase moderationCase) => _dbContext.ModerationCases.Add(moderationCase);

    public Task<ModerationCase?> GetByIdAsync(int id, CancellationToken ct) =>
        _dbContext.ModerationCases.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<ModerationCase>> GetOpenAsync(CancellationToken ct) =>
        await _dbContext.ModerationCases
            .Where(c => c.Status == ModerationCaseStatus.Pending)
            .ToListAsync(ct);
}
