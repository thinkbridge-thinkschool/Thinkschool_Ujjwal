namespace QuotesApi.Models;

public class Collection
{
    public int Id { get; private set; }
    public string Name { get; private set; }
    public int OwnerId { get; private set; }
    public string? OwnerUserId { get; private set; }

    private readonly List<CollectionItem> _items = new();
    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    private Collection() { }

    public Collection(string name, int ownerId, string? ownerUserId = null)
    {
        SetName(name);
        OwnerId = ownerId;
        OwnerUserId = ownerUserId;
    }

    public void AddItem(int quoteId, DateTimeOffset addedAt)
{
    if (_items.Count >= 50)
        throw new InvalidOperationException("A collection cannot contain more than 50 items.");
    if (_items.Any(i => i.QuoteId == quoteId))
        throw new InvalidOperationException("Duplicate quotes are not allowed in a collection.");
    _items.Add(new CollectionItem(quoteId, addedAt));
}

    public void RemoveItem(int quoteId)
{
    var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);
    if (item is null)
        throw new InvalidOperationException($"No item with quote id {quoteId} exists in this collection.");
    _items.Remove(item);
}

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.");

        if (name.Length < 3 || name.Length > 80)
            throw new ArgumentException("Name must be between 3 and 80 characters.");

        Name = name;
    }
}