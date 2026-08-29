using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Integration;

public class CollectionTests
{
    [Fact]
    public void AddItem_UsesInjectedClock_ForTimestamp()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(fixedTime);
        var collection = new Collection("Test Collection", ownerId: 1);

        // Act
        collection.AddItem(quoteId: 42, clock.UtcNow);

        // Assert
        var item = collection.Items.First();
        Assert.Equal(fixedTime, item.AddedAt);
    }
}