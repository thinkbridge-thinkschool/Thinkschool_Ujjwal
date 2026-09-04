using QuoteHub.SharedKernel;

namespace QuoteHub.Curation.Domain;

// The core aggregate. Owns its items exclusively through AddItem/RemoveItem
// (owner-driven, slot-changing) and ApplyModerationDecision (Moderation-
// driven, slot-preserving). See DESIGN.md for why those two are kept
// separate rather than folded into one "update visibility" method.
public sealed class Collection : AggregateRoot<int>
{
    public const int MinNameLength = 1;
    public const int MaxNameLength = 80;
    public const int MaxSlots = 50;

    public string Name { get; private set; } = string.Empty;
    public int OwnerId { get; private set; }
    public string? OwnerUserId { get; private set; }

    private readonly List<CollectionItem> _items = new();

    // Every slot, including tombstoned (hidden) ones. This is what MaxSlots
    // is measured against - a hidden item still occupies its slot and
    // still costs budget. That cost is deliberate: see DESIGN.md.
    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    // What a reader is shown. Excludes tombstoned items without disturbing
    // their position among Items, so a later restore comes back in place.
    public IEnumerable<CollectionItem> VisibleItems => _items.Where(i => i.Visibility == QuoteVisibility.Visible);

    public int TotalSlots => _items.Count;

    private Collection() { } // EF

    private Collection(string name, int ownerId, string? ownerUserId)
    {
        Name = name;
        OwnerId = ownerId;
        OwnerUserId = ownerUserId;
    }

    public static Result<Collection> Create(string name, int ownerId, string? ownerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<Collection>.Failure("Name is required.");
        if (name.Length < MinNameLength || name.Length > MaxNameLength)
            return Result<Collection>.Failure($"Name must be between {MinNameLength} and {MaxNameLength} characters.");

        return Result<Collection>.Success(new Collection(name, ownerId, ownerUserId));
    }

    // Owner adds a quote, capturing a read-model copy of it at this
    // moment (see DESIGN.md's boundary decision - Curation does not call
    // Moderation/Authoring at read time).
    public Result AddItem(int quoteId, string authorName, string textSnippet, QuoteVisibility visibility, DateTimeOffset addedAt)
    {
        if (_items.Count >= MaxSlots)
            return Result.Failure($"A collection cannot contain more than {MaxSlots} items.");
        if (_items.Any(i => i.QuoteId == quoteId))
            return Result.Failure("Duplicate quotes are not allowed in a collection.");

        _items.Add(new CollectionItem(quoteId, authorName, textSnippet, visibility, addedAt));
        return Result.Success();
    }

    // Owner-driven and destructive: the slot is freed and MaxSlots budget
    // is returned. Never used for moderation - a hide must never look like
    // this to the owner's curated ordering.
    public Result RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);
        if (item is null)
            return Result.Failure($"No item with quote id {quoteId} exists in this collection.");

        _items.Remove(item);
        return Result.Success();
    }

    // Reacts to a QuoteModerationDecided integration event. Slot-count
    // never changes here - only the item's rendered visibility does. This
    // is the tombstone: the count invariant Moderation must never violate.
    public Result ApplyModerationDecision(int quoteId, QuoteVisibility visibility)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);
        if (item is null)
            return Result.Failure($"No item with quote id {quoteId} exists in this collection.");

        item.ApplyVisibility(visibility);
        return Result.Success();
    }
}
