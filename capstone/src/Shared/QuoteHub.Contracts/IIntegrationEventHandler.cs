namespace QuoteHub.Contracts;

// Implemented by a module's Application layer for events it subscribes
// to (e.g. Curation.Application implements
// IIntegrationEventHandler&lt;QuoteModerationDecided&gt;). Nothing in this
// scaffold wires these handlers to a live subscriber - see
// day-20/README.md (../../day-20 relative to the repo root) for the
// intended relay shape (OutboxRelay: poll for unprocessed outbox rows,
// deserialize, dispatch, mark processed). This capstone stops at the
// handler stub deliberately: "scaffold the outbox table... and handler
// stubs. Do NOT build a full relay."
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
