using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuoteHub.Moderation.Domain;

namespace QuoteHub.Moderation.Infrastructure;

public sealed class ModerationCaseConfiguration : IEntityTypeConfiguration<ModerationCase>
{
    public void Configure(EntityTypeBuilder<ModerationCase> builder)
    {
        builder.ToTable("ModerationCases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.QuoteId).IsRequired();
        builder.Property(c => c.ReportedByUserId).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.ReportedAt).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().IsRequired();
        builder.Property(c => c.Outcome).HasConversion<string>();
        builder.Ignore(c => c.DomainEvents);
    }
}
