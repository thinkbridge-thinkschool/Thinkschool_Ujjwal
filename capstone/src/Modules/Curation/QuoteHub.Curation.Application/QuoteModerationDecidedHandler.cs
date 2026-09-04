using QuoteHub.Contracts;
using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Application;

// Reacts to a moderation decision by updating the read-model copy on
// every collection currently holding this quote - never the slot count.
// This is the handler stub the brief asks for; wiring it to a live
// subscriber is out of scope (see IIntegrationEventHandler.cs).
public sealed class QuoteModerationDecidedHandler : IIntegrationEventHandler<QuoteModerationDecided>
{
    private readonly ICollectionRepository _repository;

    public QuoteModerationDecidedHandler(ICollectionRepository repository)
    {
        _repository = repository;
    }

    public async Task HandleAsync(QuoteModerationDecided integrationEvent, CancellationToken ct)
    {
        var visibility = integrationEvent.Decision == ModerationDecision.Hidden
            ? QuoteVisibility.Hidden
            : QuoteVisibility.Visible;

        var affected = await _repository.GetContainingQuoteAsync(integrationEvent.QuoteId, ct);
        foreach (var collection in affected)
        {
            collection.ApplyModerationDecision(integrationEvent.QuoteId, visibility);
        }
    }
}
