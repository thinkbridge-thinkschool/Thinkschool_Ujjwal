namespace QuoteHub.SharedKernel;

// Identity-based equality for anything with a stable Id - the standard
// DDD distinction from a value object (SharedKernel intentionally has no
// ValueObject base in this scaffold; neither module's aggregate needs one
// yet, and one gets added when a real value object shows up, not before).
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity() { }

    protected Entity(TId id)
    {
        Id = id;
    }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => (GetType(), Id).GetHashCode();
}
