using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit.Extensions;

public class InfrastructureExtensionsTests
{
    [Fact]
    public void AddInfrastructure_NoConnectionStringConfigured_FallsBackToDefaultSqliteFile()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        db.Database.GetConnectionString().Should().Be("Data Source=quotes.db");
    }

    [Fact]
    public void AddInfrastructure_ConnectionStringConfigured_UsesConfiguredValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Data Source=configured.db"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        db.Database.GetConnectionString().Should().Be("Data Source=configured.db");
    }
}
