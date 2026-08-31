using Microsoft.AspNetCore.Mvc;
using QuotesApi.BackgroundJobs;
using QuotesApi.Clients;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using QuotesApi.Authorization;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Security.Claims;


namespace QuotesApi.Extensions;

public static class EndpointExtensions
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        var auth = app.MapGroup("/api/auth");

auth.MapPost("/register", async (RegisterRequest request, QuotesDbContext db, IJwtTokenService tokenService, IOptions<JwtOptions> jwtOptions, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    if (request.Password.Length < 8)
        return Results.BadRequest(new { error = "Password must be at least 8 characters." });

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var alreadyExists = await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
    if (alreadyExists)
        return Results.Conflict(new { error = "An account with this email already exists." });

    var user = User.Create(normalizedEmail, request.Password);
    db.Users.Add(user);
    // Flush first: user.Id is DB-generated and isn't populated until
    // SaveChanges runs, but the token's sub claim (and the refresh token's
    // FK) need the real Id, not the pre-insert default.
    await db.SaveChangesAsync(ct);

    var accessToken = tokenService.GenerateAccessToken(user);
    var refreshTokenPlain = tokenService.GenerateRefreshToken();
    var refreshTokenEntity = RefreshToken.Create(refreshTokenPlain, user.Id, DateTimeOffset.UtcNow.Add(jwtOptions.Value.RefreshTokenLifetime));
    db.RefreshTokens.Add(refreshTokenEntity);
    await db.SaveChangesAsync(ct);

    var response = new LoginResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshTokenPlain,
        ExpiresIn = tokenService.AccessTokenMinutes * 60
    };

    return Results.Created("/api/auth/login", response);
});

auth.MapPost("/login", async (LoginRequest request, QuotesDbContext db, IJwtTokenService tokenService, IOptions<JwtOptions> jwtOptions, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    if (user is null || !user.VerifyPassword(request.Password))
        return Results.Unauthorized();

    var accessToken = tokenService.GenerateAccessToken(user);
    var refreshTokenPlain = tokenService.GenerateRefreshToken();

    var refreshTokenEntity = RefreshToken.Create(refreshTokenPlain, user.Id, DateTimeOffset.UtcNow.Add(jwtOptions.Value.RefreshTokenLifetime));
    db.RefreshTokens.Add(refreshTokenEntity);
    await db.SaveChangesAsync(ct);

    var response = new LoginResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshTokenPlain,
        ExpiresIn = tokenService.AccessTokenMinutes * 60
    };

    return Results.Ok(response);
});

auth.MapPost("/refresh", async (RefreshRequest request, QuotesDbContext db, IJwtTokenService tokenService, IOptions<JwtOptions> jwtOptions, ILogger<Program> logger, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
        return Results.BadRequest(new { error = "Refresh token is required." });

    var presentedHash = RefreshToken.Hash(request.RefreshToken);
    var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == presentedHash, ct);

    if (storedToken is null)
        return Results.Unauthorized();

    // Reuse detection: token was already revoked/replaced, but is being presented again
    if (storedToken.RevokedAt is not null)
    {
        logger.LogWarning("SECURITY: Refresh token reuse detected for user {UserId}. Revoking entire token family.", storedToken.UserId);

        // Revoke every active refresh token for this user (kills the whole chain)
        var allUserTokens = await db.RefreshTokens
            .Where(t => t.UserId == storedToken.UserId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var t in allUserTokens)
            t.Revoke();

        await db.SaveChangesAsync(ct);

        return Results.Unauthorized();
    }

    if (storedToken.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.Unauthorized();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == storedToken.UserId, ct);
    if (user is null)
        return Results.Unauthorized();

    // Rotate: mint new pair, revoke the old one, link them
    var newAccessToken = tokenService.GenerateAccessToken(user);
    var newRefreshTokenPlain = tokenService.GenerateRefreshToken();
    var newRefreshTokenEntity = RefreshToken.Create(newRefreshTokenPlain, user.Id, DateTimeOffset.UtcNow.Add(jwtOptions.Value.RefreshTokenLifetime));

    db.RefreshTokens.Add(newRefreshTokenEntity);
    await db.SaveChangesAsync(ct);

    storedToken.Revoke(newRefreshTokenEntity.TokenHash);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new LoginResponse
    {
        AccessToken = newAccessToken,
        RefreshToken = newRefreshTokenPlain,
        ExpiresIn = tokenService.AccessTokenMinutes * 60
    });
});

        group.MapGet("", async (IQuoteRepository repo, CancellationToken ct, int page = 1, int size = 10) =>
        {
            if (page < 1) page = 1;
            if (size < 1) size = 10;
            if (size > 100) size = 100;
            var quotes = await repo.GetPagedAsync(page, size, ct);
            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var quote = await repo.GetByIdAsync(id, ct);
            return quote is null ? Results.NotFound() : Results.Ok(quote);
        });

        // Anonymous - matches /health's reasoning: a caller of this endpoint has no
        // more control over the upstream than a container probe does, and an upstream
        // outage must surface as 503, never as an unhandled-exception 500.
        group.MapGet("/random", async (IRandomQuoteClient client, ILogger<Program> logger, CancellationToken ct) =>
        {
            try
            {
                var quote = await client.GetRandomQuoteAsync(ct);
                return Results.Ok(quote);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
            {
                logger.LogError(ex, "Random quote upstream unavailable after retries.");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        group.MapPost("", async (CreateQuoteRequest request, ClaimsPrincipal user, IQuoteRepository repo, IBackgroundTaskQueue taskQueue, ILogger<Program> logger, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Author))
                errors["author"] = new[] { "Author is required." };
            if (string.IsNullOrWhiteSpace(request.Text))
                errors["text"] = new[] { "Text is required." };

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var callerId = user.FindFirst("oid")?.Value ?? user.FindFirst("sub")?.Value;
            var callerDisplayName = user.FindFirst("email")?.Value ?? user.FindFirst("preferred_username")?.Value;
            var quote = new Quote
            {
                Author = request.Author,
                Text = request.Text,
                CreatedByUserId = callerId,
                CreatedBy = callerDisplayName,
            };
            var created = await repo.AddAsync(quote, ct);

            // Fire-and-forget: AuditLogWorker (BackgroundJobs/) writes this
            // row after this response has already gone out - the request
            // never awaits it. A full queue must never turn a successful
            // quote creation into a client-visible failure, so a rejected
            // enqueue is only ever logged here, not surfaced as an error.
            // See day-18/README.md for the full design.
            var quoteId = created.Id;
            var enqueued = taskQueue.TryEnqueue(async (sp, workCt) =>
            {
                var db = sp.GetRequiredService<QuotesDbContext>();
                var clock = sp.GetRequiredService<IClock>();
                var jobLogger = sp.GetRequiredService<ILogger<AuditLogWorker>>();

                jobLogger.LogInformation("Audit write starting for quote {QuoteId}.", quoteId);
                db.AuditLogs.Add(new AuditLog
                {
                    QuoteId = quoteId,
                    CreatedByUserId = callerId,
                    CreatedAt = clock.UtcNow,
                });
                await db.SaveChangesAsync(workCt);
                jobLogger.LogInformation("Audit write finished for quote {QuoteId}.", quoteId);
            });

            if (!enqueued)
            {
                logger.LogWarning("Audit queue full - dropped audit entry for quote {QuoteId}.", quoteId);
            }

            return Results.Created($"/api/quotes/{created.Id}", created);
        }).RequireAuthorization("can-edit-quotes");

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, IQuoteRepository repo, IAuthorizationService authService, CancellationToken ct) =>
        {
            var quote = await repo.GetByIdAsync(id, ct);
            if (quote is null)
                return Results.NotFound();

            var authResult = await authService.AuthorizeAsync(user, quote, new MustOwnQuoteRequirement());
            if (!authResult.Succeeded)
                return Results.Forbid();

            await repo.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("can-delete-quotes");

        var collections = app.MapGroup("/collections");

        collections.MapPost("", async (CreateCollectionRequest request, ClaimsPrincipal user, ICollectionRepository repo, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 3 || request.Name.Length > 80)
                errors["name"] = new[] { "Name is required and must be between 3 and 80 characters." };

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var ownerUserId = user.FindFirst("oid")?.Value ?? user.FindFirst("sub")?.Value;

            var collection = new Collection(request.Name, ownerId: 0, ownerUserId: ownerUserId);
            var created = await repo.AddAsync(collection, ct);
            return Results.Created($"/collections/{created.Id}", created);
        }).RequireAuthorization("can-edit-quotes");

        collections.MapPost("/{id:int}/items", async (int id, AddCollectionItemRequest request, ClaimsPrincipal user, ICollectionRepository repo, IAuthorizationService authService, IClock clock, CancellationToken ct) =>
        {
            if (request.QuoteId <= 0)
                return Results.BadRequest(new { error = "QuoteId is required." });
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null)
                return Results.NotFound();

            var authResult = await authService.AuthorizeAsync(user, collection, new MustOwnCollectionRequirement());
            if (!authResult.Succeeded)
                return Results.Forbid();

            try
            {
                collection.AddItem(request.QuoteId, clock.UtcNow);
                await repo.UpdateAsync(collection, ct);
                return Results.Ok(collection);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("can-edit-quotes");

        collections.MapDelete("/{id:int}/items/{quoteId:int}", async (int id, int quoteId, ClaimsPrincipal user, ICollectionRepository repo, IAuthorizationService authService, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null)
                return Results.NotFound();

            var authResult = await authService.AuthorizeAsync(user, collection, new MustOwnCollectionRequirement());
            if (!authResult.Succeeded)
                return Results.Forbid();

            try
            {
                collection.RemoveItem(quoteId);
                await repo.UpdateAsync(collection, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        }).RequireAuthorization("can-edit-quotes");
    }
}