using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.HealthChecks;
using QuotesApi.Middleware;
using QuotesApi.Models;
using Serilog;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSerilog();
builder.ConfigureTracing();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddRandomQuoteClient(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

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