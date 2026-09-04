namespace QuoteHub.SharedKernel;

// An Entity that is also a transaction/consistency boundary - the only
// kind of object a repository saves directly, and the only kind that
// accumulates domain events for its own module's Application layer to
// react to after a successful SaveChanges. Collection (Curation.Domain)
// is this scaffold's one real example.
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot() { }

    protected AggregateRoot(TId id) : base(id) { }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
