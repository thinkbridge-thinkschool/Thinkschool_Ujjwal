using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

[Collection("EnvironmentMutatingTests")]
public class AuthEndpointTests : IDisposable
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

    private async Task<User> CreateDbUserAsync(string? email = null, string password = "Password123!")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var user = User.Create(email ?? $"{Guid.NewGuid():N}@example.com", password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Login_EmptyEmail_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "Password123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = $"{Guid.NewGuid():N}@example.com", password = "Password123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_BlankToken_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        var user = await CreateDbUserAsync();
        const string plainToken = "expired-refresh-token-value";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var expired = RefreshToken.Create(plainToken, user.Id, DateTimeOffset.UtcNow.AddDays(-1));
            db.RefreshTokens.Add(expired);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = plainToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_UserNoLongerExists_Returns401()
    {
        var user = await CreateDbUserAsync();
        const string plainToken = "orphaned-refresh-token-value";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var token = RefreshToken.Create(plainToken, user.Id, DateTimeOffset.UtcNow.AddDays(7));
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync();

            var trackedUser = await db.Users.FirstAsync(u => u.Id == user.Id);
            db.Users.Remove(trackedUser);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = plainToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_EmptyText_Returns400WithProblemDetailsNamingTextField()
    {
        var client = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));

        var response = await client.PostAsJsonAsync("/api/quotes", new { author = "Author", text = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>();
        Assert.Contains("text", problem!.Errors.Keys);
    }

    [Fact]
    public async Task DeleteQuote_NotFound_Returns404()
    {
        var client = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write", "quotes.delete"));

        var response = await client.DeleteAsync("/api/quotes/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCollection_NameTooShort_Returns400()
    {
        var client = CreateClient(_factory.MintToken(Guid.NewGuid().ToString(), "quotes.write"));

        var response = await client.PostAsJsonAsync("/collections", new { name = "ab" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Whoami_Authenticated_ReturnsValidatedByInternalAndSubject()
    {
        var subject = Guid.NewGuid().ToString();
        var client = CreateClient(_factory.MintToken(subject, "quotes.read"));

        var response = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhoamiDto>();
        Assert.Equal("Internal", body!.ValidatedBy);
        Assert.Equal(subject, body.Subject);
    }

    [Fact]
    public async Task Whoami_TokenHasOidClaim_PrefersOidOverSub()
    {
        var oid = Guid.NewGuid().ToString();
        var sub = Guid.NewGuid().ToString();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PolicyTestFactory.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim("oid", oid),
            new Claim(JwtRegisteredClaimNames.Sub, sub),
            new Claim("scope", "quotes.read")
        };
        var jwt = new JwtSecurityToken(
            issuer: PolicyTestFactory.JwtIssuer,
            audience: PolicyTestFactory.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        var client = CreateClient(token);
        var response = await client.GetAsync("/api/auth/whoami");

        var body = await response.Content.ReadFromJsonAsync<WhoamiDto>();
        Assert.Equal(oid, body!.Subject);
    }

    [Fact]
    public async Task Whoami_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record WhoamiDto(string? ValidatedBy, string? Subject, string? Name, string? Scopes);
}
