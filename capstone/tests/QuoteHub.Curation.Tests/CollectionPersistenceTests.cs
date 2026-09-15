using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QuoteHub.Curation.Domain;
using QuoteHub.Curation.Infrastructure;

namespace QuoteHub.Curation.Tests;

// Unlike CollectionTests.cs (pure in-memory aggregate behavior), this hits
// the real Azure SQL database (see ConnectionStrings:QuoteHub in
// QuoteHub.Api's user-secrets) through CurationDbContext/
// CollectionConfiguration exactly as QuoteHub.Api would. It exists because
// the owned-collection mapping through _items's backing field, and the
// QuoteId-as-key mapping, are exactly the kind of thing that compiles and
// passes against nothing (or an in-memory provider) and then fails - or
// silently corrupts data - on first contact with a real relational engine.
public class CollectionPersistenceTests
{
    private static DbContextOptions<CurationDbContext> BuildOptions()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<CollectionPersistenceTests>()
            .Build();

        var connectionString = config.GetConnectionString("QuoteHub")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:QuoteHub is not set. Run 'dotnet user-secrets set " +
                "ConnectionStrings:QuoteHub \"<real Azure SQL connection string>\" " +
                "--project src/QuoteHub.Api' - this test round-trips through a real " +
                "database on purpose, not an in-memory context.");

        return new DbContextOptionsBuilder<CurationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    [Fact]
    public async Task Collection_survives_create_save_reload_against_real_sql_server()
    {
        var options = BuildOptions();
        int collectionId;

        using (var db = new CurationDbContext(options))
        {
            var created = Collection.Create("Stoic Favorites", ownerId: 42, ownerUserId: "user-abc");
            Assert.True(created.IsSuccess);
            var collection = created.Value;

            Assert.True(collection.AddItem(1001, "Marcus Aurelius", "You have power over your mind.", QuoteVisibility.Visible, DateTimeOffset.UtcNow).IsSuccess);
            Assert.True(collection.AddItem(1002, "Seneca", "Luck is what happens when preparation meets opportunity.", QuoteVisibility.Visible, DateTimeOffset.UtcNow).IsSuccess);

            db.Collections.Add(collection);
            await db.SaveChangesAsync();
            collectionId = collection.Id;
            Assert.True(collectionId > 0); // DB-assigned surrogate key
        }

        try
        {
            // Reload in a fresh context (no first-level cache) and apply a
            // moderation decision, exactly as QuoteModerationDecidedHandler
            // would - a hide must free no slot.
            using (var db = new CurationDbContext(options))
            {
                var reloaded = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
                Assert.NotNull(reloaded);
                Assert.True(reloaded!.ApplyModerationDecision(1002, QuoteVisibility.Hidden).IsSuccess);
                await db.SaveChangesAsync();
            }

            // Reload again, in yet another fresh context, and assert the
            // aggregate came back intact.
            using (var db = new CurationDbContext(options))
            {
                var final = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);

                Assert.NotNull(final);
                Assert.Equal("Stoic Favorites", final!.Name);
                Assert.Equal(42, final.OwnerId);
                Assert.Equal("user-abc", final.OwnerUserId);

                // The tombstone rule: TotalSlots counts the hidden item,
                // VisibleItems does not.
                Assert.Equal(2, final.TotalSlots);
                var visible = Assert.Single(final.VisibleItems);
                Assert.Equal(1001, visible.QuoteId);
                Assert.Equal("Marcus Aurelius", visible.AuthorName);
                Assert.Equal("You have power over your mind.", visible.TextSnippet);

                var hidden = final.Items.Single(i => i.QuoteId == 1002);
                Assert.Equal(QuoteVisibility.Hidden, hidden.Visibility);
                Assert.Equal("Seneca", hidden.AuthorName); // read-model copy survived, not just the flag
            }
        }
        finally
        {
            using var cleanup = new CurationDbContext(options);
            var toDelete = await cleanup.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
            if (toDelete is not null)
            {
                cleanup.Collections.Remove(toDelete);
                await cleanup.SaveChangesAsync();
            }
        }
    }
}
