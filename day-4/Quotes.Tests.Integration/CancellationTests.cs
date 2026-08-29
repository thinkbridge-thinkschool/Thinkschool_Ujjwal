using System.Net.Http.Json;
using Xunit;

namespace Quotes.Tests.Integration;

[Collection("EnvironmentMutatingTests")]
public class CancellationTests : IClassFixture<PolicyTestFactory>
{
    private readonly PolicyTestFactory _factory;

    public CancellationTests(PolicyTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateCollection_WhenCancelled_ThrowsTaskCanceledException()
    {
        var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource();

        var request = new { name = "Cancel Test", ownerId = 1 };

        // Cancel immediately, before the request completes
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.PostAsJsonAsync("/collections", request, cts.Token);
        });
    }
}