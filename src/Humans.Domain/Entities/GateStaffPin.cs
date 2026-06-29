using NodaTime;

namespace Humans.Domain.Entities;

/// <summary>
/// A gate staffer's personal device PIN, owned by the Gate section (<c>gate_staff_pins</c>).
/// Set once on first use of the shared terminal and reused across shifts/days to (a) confirm
/// who is taking over the scanner on a shift change and (b) authorize a supervisor override.
/// One row per Humans user — <see cref="UserId"/> is a bare user-id key (no navigation), per
/// the cross-section linkage rule; the staffer is a Humans user stitched via services.
/// Only the hash is stored; the 4-digit PIN itself is never persisted.
/// </summary>
public class GateStaffPin
{
    /// <summary>The Humans user this PIN belongs to. Primary key — one PIN per person.</summary>
    public Guid UserId { get; init; }

    /// <summary>Hash of the staffer's PIN (never the PIN itself). Verified on take-over and override.</summary>
    public string PinHash { get; set; } = string.Empty;

    /// <summary>When the PIN was first set.</summary>
    public Instant CreatedAt { get; init; }

    /// <summary>When the PIN was last set/reset.</summary>
    public Instant UpdatedAt { get; set; }
}
