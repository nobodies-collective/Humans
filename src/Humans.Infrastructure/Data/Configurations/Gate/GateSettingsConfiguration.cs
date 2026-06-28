using Humans.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humans.Infrastructure.Data.Configurations.Gate;

public sealed class GateSettingsConfiguration : IEntityTypeConfiguration<GateSettings>
{
    public void Configure(EntityTypeBuilder<GateSettings> builder)
    {
        builder.ToTable("gate_settings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.GeneralEntryOpensAt).IsRequired();
        builder.Property(x => x.MinorAgeThresholdYears).IsRequired();

        // No HasData seed: the singleton row is created on first save. GetSettingsAsync
        // returns a safe default (general entry already open) until an admin sets it,
        // per no-startup-guards.
    }
}
