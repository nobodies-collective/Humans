using System.Text.Json;
using Humans.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humans.Infrastructure.Data.Configurations;

public class BarrioSettingsConfiguration : IEntityTypeConfiguration<BarrioSettings>
{
    public void Configure(EntityTypeBuilder<BarrioSettings> builder)
    {
        builder.ToTable("barrio_settings");

        builder.Property(s => s.OpenSeasons).HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<List<int>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    v => v.ToList()));

        builder.HasData(new BarrioSettings
        {
            Id = Guid.Parse("00000000-0000-0000-0010-000000000001"),
            PublicYear = 2026,
            OpenSeasons = new List<int> { 2026 }
        });
    }
}
