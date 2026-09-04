using Microsoft.EntityFrameworkCore;
using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Infrastructure;

// Everything Curation owns lives under the curation schema - no
// cross-schema FKs, no cross-schema joins. Moderation gets its own
// DbContext and schema in its own Infrastructure project; the two never
// share a DbContext or a migration.
public sealed class CurationDbContext : DbContext
{
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public CurationDbContext(DbContextOptions<CurationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("curation");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CurationDbContext).Assembly);
    }
}
