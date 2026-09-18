using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuoteHub.Curation.Infrastructure;
using QuoteHub.Moderation.Infrastructure;

namespace QuoteHub.Api.IntegrationTests;

// One real host for the whole integration test run - shared via
// ICollectionFixture (see ApiIntegrationTestCollection) rather than one
// per test class. That matters beyond startup cost: each host starts its
// own CurationOutboxRelay/ModerationOutboxRelay hosted service, and two
// of those polling the same real outbox tables at once would race each
// other. One host means exactly one of each relay for the whole run.
//
// Talks to a REAL SQL Server, never EF InMemory - locally that's the
// same Azure SQL database the rest of this capstone's dev workflow uses
// (via QuoteHub.Api's user-secrets, since WebApplicationFactory defaults
// to the Development environment); in CI it's the SQL Server service
// container, via ConnectionStrings__QuoteHub overriding user-secrets (an
// environment variable outranks user-secrets in ASP.NET Core's default
// configuration order). Either way, Database.MigrateAsync() below is
// idempotent - it creates the schema on the empty CI container and
// no-ops against Azure SQL where it's already applied.
public sealed class ApiFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<CurationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ModerationDbContext>().Database.MigrateAsync();
    }

    // Explicit implementation: WebApplicationFactory already exposes a
    // public ValueTask DisposeAsync() (IAsyncDisposable); IAsyncLifetime
    // wants a Task-returning one under the same name, so this can't be
    // implemented implicitly without a clash.
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiIntegrationTestCollection : ICollectionFixture<ApiFactoryFixture>
{
    public const string Name = "Api Integration Tests";
}
