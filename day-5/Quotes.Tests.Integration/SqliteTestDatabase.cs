using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace Quotes.Tests.Integration;

// day-28: the app's real migrations (day-5/QuotesApi/Migrations) are now
// authored against SqlServer (the production provider, since dev and
// prod share one real Azure SQL database - see day-23/params/prod.bicepparam).
// SqlServer bakes concrete store types into each migration operation at
// generation time (nvarchar(max), datetimeoffset, ...) - SQLite's
// migration executor can't parse "nvarchar(max)" ('near "max": syntax
// error', found by actually running these tests, not anticipated), so
// pointing a SQLite-configured context at Database.Migrate() with these
// same migration files fails outright.
//
// EnsureCreated() sidesteps this - it builds the schema fresh from the
// CURRENT MODEL as configured for whichever provider is active, so
// SQLite gets SQLite-appropriate DDL. The wrinkle: EnsureCreated() does
// not create __EFMigrationsHistory, and Program.cs unconditionally calls
// Database.Migrate() at startup - without a history table showing every
// migration as already applied, Migrate() would see nothing recorded and
// try to re-run InitialCreate's CREATE TABLE statements against a schema
// that already has those tables. Stamping the history table (with every
// migration ID discovered from the context, not hardcoded) after
// EnsureCreated() is what makes Migrate() see nothing pending and do
// nothing, letting the app start normally against the pre-built schema.
public static class SqliteTestDatabase
{
    public static void EnsureCreatedWithMigrationHistoryStamped(string dbPath)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using var context = new QuotesDbContext(options);

        context.Database.EnsureCreated();

        context.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)");

        foreach (var migrationId in context.Database.GetMigrations())
        {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1})",
                migrationId, "10.0.10");
        }
    }
}
