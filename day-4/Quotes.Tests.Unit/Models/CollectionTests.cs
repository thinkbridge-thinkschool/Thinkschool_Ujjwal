using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit.Models;

public class CollectionTests
{
    [Fact]
    public void Create_ValidInput_SetsNameOwnerIdAndOwnerUserId()
    {
        // Arrange
        var name = "Favourite Quotes";
        var ownerId = 42;
        var ownerUserId = "user-123";

        // Act
        var collection = new Collection(name, ownerId, ownerUserId);

        // Assert
        collection.Name.Should().Be(name);
        collection.OwnerId.Should().Be(ownerId);
        collection.OwnerUserId.Should().Be(ownerUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void Create_InvalidName_ThrowsArgumentException(string invalidName)
    {
        // Arrange
        var ownerId = 1;

        // Act
        var act = () => new Collection(invalidName, ownerId);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullName_ThrowsArgumentExceptionWithRequiredMessage()
    {
        // Arrange
        string? nullName = null;

        // Act
        var act = () => new Collection(nullName!, 1);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("Name is required.");
    }

    [Fact]
    public void Create_NameLongerThan80Characters_ThrowsArgumentException()
    {
        // Arrange
        var tooLongName = new string('a', 81);

        // Act
        var act = () => new Collection(tooLongName, 1);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("Name must be between 3 and 80 characters.");
    }

    [Fact]
    public void AddItem_NewQuoteId_AddsItemToCollection()
    {
        // Arrange
        var collection = new Collection("Favourite Quotes", 1);
        var addedAt = DateTimeOffset.UtcNow;

        // Act
        collection.AddItem(7, addedAt);

        // Assert
        collection.Items.Should().ContainSingle(i => i.QuoteId == 7 && i.AddedAt == addedAt);
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsInvalidOperationException()
    {
        // Arrange
        var collection = new Collection("Favourite Quotes", 1);
        collection.AddItem(7, DateTimeOffset.UtcNow);

        // Act
        var act = () => collection.AddItem(7, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate quotes are not allowed in a collection.");
    }

    [Fact]
    public void AddItem_AlreadyAt50Items_ThrowsInvalidOperationException()
    {
        // Arrange
        var collection = new Collection("Favourite Quotes", 1);
        for (var quoteId = 1; quoteId <= 50; quoteId++)
            collection.AddItem(quoteId, DateTimeOffset.UtcNow);

        // Act
        var act = () => collection.AddItem(51, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("A collection cannot contain more than 50 items.");
    }

    [Fact]
    public void RemoveItem_ExistingQuoteId_RemovesItemFromCollection()
    {
        // Arrange
        var collection = new Collection("Favourite Quotes", 1);
        collection.AddItem(7, DateTimeOffset.UtcNow);

        // Act
        collection.RemoveItem(7);

        // Assert
        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_AbsentQuoteId_ThrowsInvalidOperationException()
    {
        // Arrange
        var collection = new Collection("Favourite Quotes", 1);

        // Act
        var act = () => collection.RemoveItem(99);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("No item with quote id 99 exists in this collection.");
    }
}
