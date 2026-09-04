namespace QuoteHub.SharedKernel;

// Marker for something that happened inside ONE module's aggregate and
// matters to code within that same module (e.g. an application-layer
// handler reacting after SaveChanges). This is deliberately NOT how
// modules talk to each other - that's QuoteHub.Contracts's job. A domain
// event never crosses a module boundary; only a Contracts integration
// event, published via the outbox, does. Conflating the two is exactly
// how a modular monolith's boundaries erode - see DESIGN.md.
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
