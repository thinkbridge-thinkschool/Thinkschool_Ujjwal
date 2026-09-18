namespace QuoteHub.Contracts;

// Implemented by a module's Application layer for events it subscribes
// to (e.g. Curation.Application implements
// IIntegrationEventHandler&lt;QuoteModerationDecided&gt;). Each module
// registers its handler against this interface, not its concrete type
// (see each module's DependencyInjection.cs) - that's what lets
// CurationOutboxRelay and ModerationOutboxRelay dispatch to the other
// module's handler without ever referencing that module's
// Domain/Application/Infrastructure directly.
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
