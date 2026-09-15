using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;

namespace Quotes.Tests.Integration;

// Same env-var-override trick as PolicyTestFactory (see comment there): Program.cs
// reads Jwt:*/Entra:* into local variables before builder.Build() runs, so these have
// to reach the process environment before the host is forced to build.
public class ProductionSeedTestFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"quotesapi-seed-tests-{Guid.NewGuid():N}.db");

    public ProductionSeedTestFactory()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Jwt__Key"] = "seed-tests-signing-key-do-not-use-in-prod!",
            ["Jwt__Issuer"] = "QuotesApi.Tests",
            ["Jwt__Audience"] = "QuotesApi.Tests.Clients",
            ["Jwt__AccessTokenLifetime"] = "00:15:00",
            ["Jwt__RefreshTokenLifetime"] = "7.00:00:00",
            ["Entra__TenantId"] = "00000000-0000-0000-0000-000000000000",
            ["Entra__Audience"] = "00000000-0000-0000-0000-000000000001",
            ["ConnectionStrings__Default"] = $"Data Source={_dbPath}"
        };

        // day-28: see SqliteTestDatabase.cs - pre-creates the schema (and
        // stamps migration history) before the host below ever runs
        // Program.cs's Database.Migrate(), which would otherwise try to
        // apply the SqlServer-authored migrations' DDL against SQLite.
        SqliteTestDatabase.EnsureCreatedWithMigrationHistoryStamped(_dbPath);

        var originalValues = new Dictionary<string, string?>();
        foreach (var (key, value) in overrides)
        {
            originalValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            _ = Server;
        }
        finally
        {
            foreach (var (key, original) in originalValues)
                Environment.SetEnvironmentVariable(key, original);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        // day-28: AddInfrastructure now calls UseSqlServer unconditionally
        // (the app's real cutover from SQLite) - "Data Source=..." is
        // actually a VALID SqlClient connection-string key too (an alias
        // for Server=), so without this override the app would try to
        // open a network connection to a SQL Server literally named
        // after this temp file path and hang until it timed out, instead
        // of ever reaching ConnectionStrings__Default's intended SQLite
        // file. Swapped back to SQLite here specifically for the test
        // double: what this test verifies (seeding behavior) doesn't
        // depend on which relational provider is under it, and a real
        // SQL Server dependency isn't worth paying for in a fast unit-ish
        // test.
        builder.ConfigureServices(services =>
        {
            // Removing just DbContextOptions<QuotesDbContext> isn't
            // enough - AddDbContext also registers an
            // IDbContextOptionsConfiguration<QuotesDbContext> configurator
            // separately, and EF applies EVERY registered configurator to
            // the same options object rather than the last one winning.
            // Without removing this too, AddInfrastructure's UseSqlServer
            // configurator and this UseSqlite one both apply, and EF
            // throws "only a single database provider can be registered" -
            // found by actually running this, not anticipated.
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            // The configurator type EF registers alongside DbContextOptions
            // is internal to EF Core (not publicly nameable here) - matched
            // by name instead of by type, since removing only
            // DbContextOptions<QuotesDbContext> leaves it behind and EF
            // applies every registered configurator to the same options
            // object rather than letting the last one win.
            foreach (var descriptor in services
                .Where(d => d.ServiceType.IsGenericType
                    && d.ServiceType.Name.Contains("DbContextOptionsConfiguration")
                    && d.ServiceType.GenericTypeArguments.Contains(typeof(QuotesDbContext)))
                .ToList())
            {
                services.Remove(descriptor);
            }
            // ConfigureWarnings suppresses PendingModelChangesWarning: the
            // stored migration snapshot was captured against the
            // SqlServer provider (the real, production one), so comparing
            // it against this deliberately-different SQLite-configured
            // model always reports a mismatch - a real signal for a
            // genuinely out-of-sync migration, but noise for a test that
            // intentionally swaps providers.
            services.AddDbContext<QuotesDbContext>(options => options
                .UseSqlite($"Data Source={_dbPath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

[Collection("EnvironmentMutatingTests")]
public class SeedBehaviorTests : IDisposable
{
    private readonly ProductionSeedTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // "Seeded credentials must never be created in a deployed environment" (Program.cs) —
    // this is the invariant behind that comment: a fresh database in a non-Development
    // environment must come up with no seeded user.
    [Fact]
    public async Task Startup_InProductionEnvironment_DoesNotSeedTestUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        Assert.False(await db.Users.AnyAsync());
    }
}
