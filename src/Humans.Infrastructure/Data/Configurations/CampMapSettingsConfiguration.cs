using Humans.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humans.Infrastructure.Data.Configurations;

public class CampMapSettingsConfiguration : IEntityTypeConfiguration<CampMapSettings>
{
    public void Configure(EntityTypeBuilder<CampMapSettings> builder)
    {
        builder.ToTable("camp_map_settings");

        builder.HasIndex(s => s.Year).IsUnique();

        builder.Property(s => s.LimitZoneGeoJson).HasColumnType("text");
    }
}
