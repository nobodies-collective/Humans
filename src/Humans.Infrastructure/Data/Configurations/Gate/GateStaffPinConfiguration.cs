using Humans.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humans.Infrastructure.Data.Configurations.Gate;

public sealed class GateStaffPinConfiguration : IEntityTypeConfiguration<GateStaffPin>
{
    public void Configure(EntityTypeBuilder<GateStaffPin> builder)
    {
        builder.ToTable("gate_staff_pins");

        // One PIN per Humans user — UserId is the key (a bare cross-section id, not an FK).
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).ValueGeneratedNever();

        builder.Property(x => x.PinHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
