using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Tests;

// No database, no HTTP - pure aggregate behavior. Every method here maps
// to one invariant named in DESIGN.md.
public class CollectionTests
{
    private static Collection CreateValid(string name = "My Collection") =>
        Collection.Create(name, ownerId: 1).Value;

    [Fact]
    public void Create_with_valid_name_succeeds()
    {
        var result = Collection.Create("Favorites", ownerId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Favorites", result.Value.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_with_missing_name_fails(string? name)
    {
        var result = Collection.Create(name!, ownerId: 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_with_name_at_minimum_length_succeeds()
    {
        var result = Collection.Create(new string('a', Collection.MinNameLength), ownerId: 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_with_name_at_maximum_length_succeeds()
    {
        var result = Collection.Create(new string('a', Collection.MaxNameLength), ownerId: 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_with_name_over_maximum_length_fails()
    {
        var result = Collection.Create(new string('a', Collection.MaxNameLength + 1), ownerId: 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Collection_has_no_public_constructor()
    {
        // Construction only via Collection.Create() - a public
        // constructor here would let callers bypass the name invariant.
        var constructors = typeof(Collection).GetConstructors();

        Assert.Empty(constructors);
    }

    [Fact]
    public void AddItem_succeeds_and_appears_in_items_and_visible_items()
    {
        var collection = CreateValid();

        var result = collection.AddItem(quoteId: 1, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, collection.TotalSlots);
        Assert.Single(collection.VisibleItems);
    }

    [Fact]
    public void AddItem_fails_when_collection_already_has_max_slots()
    {
        var collection = CreateValid();
        for (var i = 0; i < Collection.MaxSlots; i++)
            Assert.True(collection.AddItem(i, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow).IsSuccess);

        var result = collection.AddItem(Collection.MaxSlots, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(Collection.MaxSlots, collection.TotalSlots);
    }

    [Fact]
    public void AddItem_fails_on_duplicate_quote_id()
    {
        var collection = CreateValid();
        collection.AddItem(1, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        var result = collection.AddItem(1, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(1, collection.TotalSlots);
    }

    [Fact]
    public void RemoveItem_removes_an_existing_item_and_frees_its_slot()
    {
        var collection = CreateValid();
        collection.AddItem(1, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        var result = collection.RemoveItem(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, collection.TotalSlots);
    }

    [Fact]
    public void RemoveItem_fails_for_a_nonexistent_item()
    {
        var collection = CreateValid();

        var result = collection.RemoveItem(999);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ApplyModerationDecision_hides_item_without_changing_total_slots()
    {
        var collection = CreateValid();
        collection.AddItem(1, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        var result = collection.ApplyModerationDecision(1, QuoteVisibility.Hidden);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, collection.TotalSlots);
        Assert.Empty(collection.VisibleItems);
    }

    [Fact]
    public void ApplyModerationDecision_restore_reverses_a_hide()
    {
        var collection = CreateValid();
        collection.AddItem(1, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);
        collection.ApplyModerationDecision(1, QuoteVisibility.Hidden);

        var result = collection.ApplyModerationDecision(1, QuoteVisibility.Visible);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, collection.TotalSlots);
        Assert.Single(collection.VisibleItems);
    }

    [Fact]
    public void ApplyModerationDecision_fails_for_a_nonexistent_item()
    {
        var collection = CreateValid();

        var result = collection.ApplyModerationDecision(999, QuoteVisibility.Hidden);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Tombstoned_item_still_counts_toward_max_slots()
    {
        // The invariant DESIGN.md calls the deliberate cost: a hidden
        // item keeps its slot, so it still counts against the 50-item
        // limit even though it renders nothing.
        var collection = CreateValid();
        for (var i = 0; i < Collection.MaxSlots; i++)
            collection.AddItem(i, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);
        collection.ApplyModerationDecision(0, QuoteVisibility.Hidden);

        var result = collection.AddItem(Collection.MaxSlots, "Author", "Snippet", QuoteVisibility.Visible, DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(Collection.MaxSlots, collection.TotalSlots);
        Assert.Equal(Collection.MaxSlots - 1, collection.VisibleItems.Count());
    }
}
