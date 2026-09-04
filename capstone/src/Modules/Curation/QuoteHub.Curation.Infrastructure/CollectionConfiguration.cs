using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuoteHub.Curation.Domain;

namespace QuoteHub.Curation.Infrastructure;

// Maps Collection/CollectionItem onto curation.Collections /
// curation.CollectionItems through their private backing fields, so the
// aggregate keeps factory-only construction and private setters instead
// of growing public mutators for EF's benefit. Not exercised against a
// live database as part of this scaffold - see the verification report
// for what was and wasn't run.
public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(Collection.MaxNameLength).IsRequired();
        builder.Property(c => c.OwnerId).IsRequired();
        builder.Property(c => c.OwnerUserId).HasMaxLength(450);

        // Computed, not persisted - derived from _items on read.
        builder.Ignore(c => c.Items);
        builder.Ignore(c => c.VisibleItems);
        builder.Ignore(c => c.TotalSlots);
        builder.Ignore(c => c.DomainEvents);

        builder.OwnsMany<CollectionItem>("_items", item =>
        {
            item.ToTable("CollectionItems");
            item.WithOwner().HasForeignKey("CollectionId");

            item.Property(i => i.Id).HasColumnName("QuoteId");
            item.HasKey(nameof(CollectionItem.Id), "CollectionId");
            item.Ignore(i => i.QuoteId);

            item.Property(i => i.AuthorName).HasMaxLength(200).IsRequired();
            item.Property(i => i.TextSnippet).HasMaxLength(500).IsRequired();
            item.Property(i => i.Visibility).HasConversion<string>().IsRequired();
            item.Property(i => i.AddedAt).IsRequired();
        });

        builder.Navigation("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
