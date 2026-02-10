using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Humans.Domain.Entities;

namespace Humans.Infrastructure.Data.Configurations;

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");

        builder.HasKey(dv => dv.Id);

        builder.Property(dv => dv.VersionNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(dv => dv.CommitSha)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(dv => dv.Content)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(dv => dv.EffectiveFrom)
            .IsRequired();

        builder.Property(dv => dv.CreatedAt)
            .IsRequired();

        builder.Property(dv => dv.ChangesSummary)
            .HasMaxLength(2000);

        builder.HasMany(dv => dv.ConsentRecords)
            .WithOne(cr => cr.DocumentVersion)
            .HasForeignKey(cr => cr.DocumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(dv => dv.LegalDocumentId);
        builder.HasIndex(dv => dv.EffectiveFrom);
        builder.HasIndex(dv => dv.CommitSha);
    }
}
