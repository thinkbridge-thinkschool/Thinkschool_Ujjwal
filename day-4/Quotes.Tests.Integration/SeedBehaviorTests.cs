using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
