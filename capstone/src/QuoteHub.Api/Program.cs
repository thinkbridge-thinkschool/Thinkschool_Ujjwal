using QuoteHub.Curation.Infrastructure;
using QuoteHub.Moderation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// The only place either module's Infrastructure is referenced - DI
// registration only. Nothing here calls into a module's internals, and
// no module references the other's projects; see
// QuoteHub.ArchitectureTests for the enforced version of that rule.
builder.Services.AddCurationInfrastructure(builder.Configuration);
builder.Services.AddModerationInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
