using Microsoft.EntityFrameworkCore;
using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Infrastructure;

public sealed class ModerationDbContext : DbContext
{
    public DbSet<ModerationCase> ModerationCases => Set<ModerationCase>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public ModerationDbContext(DbContextOptions<ModerationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("moderation");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ModerationDbContext).Assembly);
    }
}
