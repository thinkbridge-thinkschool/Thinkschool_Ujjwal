using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit.Extensions;

public class InfrastructureExtensionsTests
{
    // day-28: SQL Server cutover - there is no default-file fallback any
    // more (SQLite's "just create a local file" convenience doesn't have
    // an equivalent for a server-based provider, and papering over an
    // unconfigured connection string with a silent default would hide a
    // real deployment misconfiguration rather than fail loudly). An
    // unconfigured connection string is now genuinely null - this test
    // documents that as the actual, intended behavior, not an oversight.
    [Fact]
    public void AddInfrastructure_NoConnectionStringConfigured_ConnectionStringIsNull()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        db.Database.GetConnectionString().Should().BeNull();
    }

    [Fact]
    public void AddInfrastructure_ConnectionStringConfigured_UsesConfiguredValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=tcp:test-server.database.windows.net,1433;Database=configured-db;User Id=test;Password=test;"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        // Not an exact-match assertion: SqlClient's connection string
        // builder canonicalizes what's passed in (e.g. appends its own
        // Application Name), so the round-tripped string is not
        // byte-for-byte identical to the input even though it correctly
        // reflects the configured server/database - checking for the
        // configured values landing correctly, not for zero
        // normalization, which was never a real requirement.
        var connectionString = db.Database.GetConnectionString();
        connectionString.Should().Contain("test-server.database.windows.net");
        connectionString.Should().Contain("configured-db");
    }
}
