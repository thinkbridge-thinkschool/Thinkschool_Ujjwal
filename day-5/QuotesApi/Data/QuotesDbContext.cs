using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuotesDbContext : DbContext
{
    public QuotesDbContext(DbContextOptions<QuotesDbContext> options) : base(options) { }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The dedupe key: a subscription can only ever record ONE
        // ProcessedMessages row for a given MessageId. This is the
        // backstop for a genuine race between two competing consumers
        // landing on the same message at once - the plain read in
        // AuditSubscriptionWorker.HandleMessageAsync narrows the window,
        // this constraint closes it.
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasIndex(p => new { p.SubscriptionName, p.MessageId }).IsUnique();
        });

        // OutboxRelay's poll query filters on ProcessedAt IS NULL every
        // 5 seconds - this index is what keeps that a lookup instead of
        // a growing table scan as processed rows accumulate.
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasIndex(o => o.ProcessedAt);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(c => c.OwnerId)
                .IsRequired();

            entity.Navigation(c => c.Items)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.OwnsMany(c => c.Items, owned =>
            {
                owned.WithOwner().HasForeignKey("CollectionId");
                owned.HasKey("CollectionId", nameof(CollectionItem.QuoteId));
                owned.Property(ci => ci.QuoteId).IsRequired();
                owned.Property(ci => ci.AddedAt).IsRequired();
                owned.ToTable("CollectionItems");
            });
        });
    }
}