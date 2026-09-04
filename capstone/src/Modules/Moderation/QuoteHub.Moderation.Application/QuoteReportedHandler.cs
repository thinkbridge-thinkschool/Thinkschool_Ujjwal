using QuoteHub.Contracts;
using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Application;

// Reacts to a QuoteReported integration event by opening a moderation
// case. Async so reporting (Curation's side) stays fast and succeeds
// even when Moderation is degraded - see DESIGN.md.
public sealed class QuoteReportedHandler : IIntegrationEventHandler<QuoteReported>
{
    private readonly IModerationCaseRepository _repository;

    public QuoteReportedHandler(IModerationCaseRepository repository)
    {
        _repository = repository;
    }

    public Task HandleAsync(QuoteReported integrationEvent, CancellationToken ct)
    {
        var result = ModerationCase.Create(
            integrationEvent.QuoteId,
            integrationEvent.ReportedByUserId,
            integrationEvent.Reason,
            integrationEvent.OccurredAt);

        if (result.IsSuccess)
            _repository.Add(result.Value);

        return Task.CompletedTask;
    }
}
