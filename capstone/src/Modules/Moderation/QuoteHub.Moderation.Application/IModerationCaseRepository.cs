using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Application;

public interface IModerationCaseRepository
{
    void Add(ModerationCase moderationCase);
}
