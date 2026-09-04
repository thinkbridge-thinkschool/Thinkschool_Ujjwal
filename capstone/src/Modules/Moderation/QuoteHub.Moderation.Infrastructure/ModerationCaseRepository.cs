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
}
