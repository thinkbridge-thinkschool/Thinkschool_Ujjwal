namespace QuoteHub.Contracts;

// The ONLY vocabulary two modules share. A module publishes one of these
// via its own outbox (see IOutboxMessage / day-20's OutboxRelay, which
// this scaffold references rather than reimplements) and any other
// module may subscribe - the publisher never knows or cares who's
// listening. No module project ever references another module's
// Domain/Application/Infrastructure; this is what replaces that.
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
