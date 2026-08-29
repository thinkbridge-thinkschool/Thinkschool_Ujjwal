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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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