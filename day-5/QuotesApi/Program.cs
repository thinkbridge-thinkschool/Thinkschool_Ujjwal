using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.HealthChecks;
using QuotesApi.Middleware;
using QuotesApi.Models;
using Serilog;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// day-27: Kestrel's default (30MB) is generous enough to be a real
// resource-exhaustion vector for an API whose largest legitimate body
// (a quote: 200 + 2000 chars, or a login form) is a few KB at most. 64KB
// leaves headroom for the largest real payload plus JSON overhead
// without leaving the door open to multi-megabyte bodies from callers
// with no reason to send one.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.ConfigureSerilog();
builder.ConfigureTracing();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddRandomQuoteClient(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// day-27: without this, HttpContext.Connection.RemoteIpAddress on App
// Service is the platform's internal front-end address, not the real
// caller - every client would land in the SAME rate-limit partition
// below, meaning one abusive caller could lock out everyone else. Known
// networks/proxies cleared deliberately: App Service's front-end IPs
// aren't a small, fixed, documented set the way a self-hosted reverse
// proxy's would be, and nothing else sits in front of this app.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// day-27: /api/auth/register and /api/auth/login are unauthenticated by
// necessity - nothing else stops a brute-force credential-stuffing loop
// against them. Partitioned by caller IP (not global) so a single
// abusive source is throttled without also punishing every other user
// sharing the API. 5 requests/minute is deliberately tight for a login
// form a genuine user hits a handful of times per session at most; queue
// limit 0 rejects over-limit requests immediately (429) rather than
// holding them, since a queued brute-force attempt is still a
// brute-force attempt, just a slower one.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Dev-only: lets a locally-run Angular dev server (any localhost port) call this
// API from the browser. Never enabled outside Development.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddPolicy("AngularDev", policy =>
            policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
                  .AllowAnyMethod()
                  .AllowAnyHeader()));
}
else
{
    // The deployed Angular app lives on a different origin (Azure Static
    // Web Apps), so browser calls here are cross-origin. Cors:AllowedOrigin
    // is an App Service Application Setting, not source - it's not a
    // secret, but keeping it out of the repo means the API doesn't need a
    // redeploy if the frontend's URL ever changes. No policy is registered
    // at all if it's unset, so an unconfigured deployment fails closed
    // (no CORS headers) rather than silently allowing every origin.
    var allowedOrigin = builder.Configuration["Cors:AllowedOrigin"];
    if (!string.IsNullOrWhiteSpace(allowedOrigin))
    {
        builder.Services.AddCors(options =>
            options.AddPolicy("Swa", policy =>
                policy.WithOrigins(allowedOrigin)
                      .AllowAnyMethod()
                      .AllowAnyHeader()));
    }
}

var app = builder.Build();

// Must run before anything that reads the connection's remote IP -
// including the rate limiter below and Serilog's request logging - or
// they see the platform's internal address instead of the real caller.
app.UseForwardedHeaders();

// Correlation goes outermost so every log line - including from ExceptionMiddleware
// and the request-logging middleware - carries the request's trace id.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

// Exception handling wraps the auth middleware.
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDev");
}
else if (!string.IsNullOrWhiteSpace(app.Configuration["Cors:AllowedOrigin"]))
{
    app.UseCors("Swa");
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Container/orchestrator probe - no bearer token available, so this must stay anonymous.
app.MapHealthChecks("/health").AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    db.Database.Migrate();

    // Seeded credentials must never be created in a deployed environment.
    if (app.Environment.IsDevelopment() && !db.Users.Any())
    {
        db.Users.Add(User.Create("test@example.com", "Password123!"));
        db.SaveChanges();
    }
}

app.MapQuoteEndpoints();

// Day 21 measurement support only - not part of the product API, gated
// to Development so it never exists in a deployed environment. Lets the
// standalone load harness (day-21/) read/reset the real database-hit
// count around a run instead of assuming what the cache did from the
// outside. See day-21/README.md.
if (app.Environment.IsDevelopment())
{
    // quotesQueries is the number the load harness actually measures
    // against (day-21/README.md) - calls to QuoteRepository.GetPagedAsync
    // specifically. dbCommands is every SQL command the process has run,
    // included for general visibility, but it also counts OutboxRelay's
    // own 5-second poll queries, so it is NOT what "cold cache, N
    // concurrent requests -> 1 DB hit" is measured against.
    app.MapGet("/api/diagnostics/db-hits", (
        QuotesApi.Diagnostics.DbHitCounterInterceptor dbCommandCounter,
        QuotesApi.Diagnostics.QuoteQueryCounter quoteQueryCounter) =>
        Results.Ok(new { dbCommands = dbCommandCounter.Count, quotesQueries = quoteQueryCounter.Count }))
        .AllowAnonymous();

    app.MapPost("/api/diagnostics/db-hits/reset", (
        QuotesApi.Diagnostics.DbHitCounterInterceptor dbCommandCounter,
        QuotesApi.Diagnostics.QuoteQueryCounter quoteQueryCounter) =>
    {
        dbCommandCounter.Reset();
        quoteQueryCounter.Reset();
        return Results.NoContent();
    }).AllowAnonymous();

    // Day 22 measurement support only - drives the Redis resilience
    // pipeline directly, bypassing HybridCache/L1 entirely (a fresh
    // random key every call, so L1 can never satisfy it and the request
    // always reaches ResilientDistributedCache). Without this, proving
    // the circuit breaker's lifecycle over HTTP would mostly be fighting
    // L1's 10s local cache masking repeat GET /api/quotes calls from ever
    // reaching Redis at all. See day-22/README.md.
    app.MapPost("/api/diagnostics/redis-probe", async (Microsoft.Extensions.Caching.Distributed.IDistributedCache cache, CancellationToken ct) =>
    {
        try
        {
            var key = $"probe:{Guid.NewGuid():N}";
            await cache.SetAsync(
                key,
                "1"u8.ToArray(),
                new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) },
                ct);
            return Results.Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { ok = false, exception = ex.GetType().Name, message = ex.Message });
        }
    }).AllowAnonymous();
}

// Proof endpoint for the Day 3 exercise: reports which scheme validated the
// request, so one curl distinguishes an internal token from an Entra one.
app.MapGet("/api/auth/whoami", (ClaimsPrincipal user) => Results.Ok(new
{
    validatedBy = user.Identity?.AuthenticationType,
    subject = user.FindFirst("oid")?.Value ?? user.FindFirst("sub")?.Value,
    name = user.Identity?.Name,
    scopes = user.FindFirst("scp")?.Value
}))
.RequireAuthorization();

app.Run();

// Required for WebApplicationFactory<Program> in QuotesApi.Tests.
public partial class Program { }