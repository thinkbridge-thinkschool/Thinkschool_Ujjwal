using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Quotes.Tests.Integration;

[Collection("EnvironmentMutatingTests")]
public class CollectionItemsTests : IDisposable
{
    private readonly PolicyTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(string? token = null)
    {
        var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<CollectionDto> CreateCollectionAsync(HttpClient client, string name = "Test Collection")
    {
        var response = await client.PostAsJsonAsync("/collections", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CollectionDto>())!;
    }

    [Fact]
    public async Task AddItem_QuoteIdZeroOrLess_Returns400()
    {
        var client = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));
        var collection = await CreateCollectionAsync(client);

        var response = await client.PostAsJsonAsync($"/collections/{collection.Id}/items", new { quoteId = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_CollectionNotFound_Returns404()
    {
        var client = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));

        var response = await client.PostAsJsonAsync("/collections/999999/items", new { quoteId = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_ByOwner_Returns200AndAddsItem()
    {
        var owner = Guid.NewGuid().ToString();
        var client = CreateClient(_factory.MintToken(owner, "quotes.write"));
        var collection = await CreateCollectionAsync(client);

        var response = await client.PostAsJsonAsync($"/collections/{collection.Id}/items", new { quoteId = 7 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CollectionWithItemsDto>();
        Assert.Contains(updated!.Items, i => i.QuoteId == 7);
    }

    [Fact]
    public async Task AddItem_DuplicateQuote_Returns400()
    {
        var owner = Guid.NewGuid().ToString();
        var client = CreateClient(_factory.MintToken(owner, "quotes.write"));
        var collection = await CreateCollectionAsync(client);
        (await client.PostAsJsonAsync($"/collections/{collection.Id}/items", new { quoteId = 7 })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/collections/{collection.Id}/items", new { quoteId = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveItem_ByOwner_Returns204()
    {
        var owner = Guid.NewGuid().ToString();
        var client = CreateClient(_factory.MintToken(owner, "quotes.write"));
        var collection = await CreateCollectionAsync(client);
        (await client.PostAsJsonAsync($"/collections/{collection.Id}/items", new { quoteId = 7 })).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/collections/{collection.Id}/items/7");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveItem_CollectionNotFound_Returns404()
    {
        var client = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));

        var response = await client.DeleteAsync("/collections/999999/items/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveItem_ByNonOwner_Returns403()
    {
        var ownerClient = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));
        var collection = await CreateCollectionAsync(ownerClient);
        (await ownerClient.PostAsJsonAsync($"/collections/{collection.Id}/items", new { quoteId = 7 })).EnsureSuccessStatusCode();

        var otherClient = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));
        var response = await otherClient.DeleteAsync($"/collections/{collection.Id}/items/7");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveItem_QuoteNotInCollection_Returns404()
    {
        var owner = Guid.NewGuid().ToString();
        var client = CreateClient(_factory.MintToken(owner, "quotes.write"));
        var collection = await CreateCollectionAsync(client);

        var response = await client.DeleteAsync($"/collections/{collection.Id}/items/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record CollectionDto(int Id, string Name, int OwnerId, string? OwnerUserId);
    private record CollectionItemDto(int QuoteId, DateTimeOffset AddedAt);
    private record CollectionWithItemsDto(int Id, string Name, int OwnerId, string? OwnerUserId, List<CollectionItemDto> Items);
}
