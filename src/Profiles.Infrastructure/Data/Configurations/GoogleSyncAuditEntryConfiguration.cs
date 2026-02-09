using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Profiles.Domain.Entities;

namespace Profiles.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration for GoogleSyncAuditEntry entity.
/// This table is append-only — no updates or deletes should be performed.
/// A database trigger enforces this at the database level.
/// </summary>
public class GoogleSyncAuditEntryConfiguration : IEntityTypeConfiguration<GoogleSyncAuditEntry>
{
    public void Configure(EntityTypeBuilder<GoogleSyncAuditEntry> builder)
    {
        builder.ToTable("google_sync_audit");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Action)
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Source)
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.UserEmail)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.Role)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Timestamp)
            .IsRequired();

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(4000);

        // FK to GoogleResource with Cascade on delete
        builder.HasOne(e => e.Resource)
            .WithMany()
            .HasForeignKey(e => e.ResourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to User with SetNull on delete (survives user anonymization; UserEmail preserves identity)
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes for querying
        builder.HasIndex(e => e.ResourceId);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Action);
    }
}
