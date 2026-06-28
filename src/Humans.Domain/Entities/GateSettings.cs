using NodaTime;

namespace Humans.Domain.Entities;

/// <summary>
/// Singleton (<see cref="Id"/> always 1) configuration for the Gate section,
/// owned by it (<c>gate_settings</c>). Kept here rather than on the Shifts
/// burn-settings row so the gate-admission posture stays inside this section's
/// boundary. The general-entry cutoff is a precise <see cref="Instant"/> (UTC),
/// not a calendar date, because admission is decided to the minute as the queue
/// crosses noon.
/// </summary>
public class GateSettings
{
    /// <summary>Always 1 — singleton row.</summary>
    public int Id { get; init; }

    /// <summary>
    /// The instant general entry opens to every valid ticket. Before it, a valid
    /// ticket also needs an Early Entry grant covering today; at or after it,
    /// Early Entry is irrelevant. Default <see cref="Instant.MinValue"/> means
    /// "general entry already open" (no Early Entry gating) until an admin sets it.
    /// </summary>
    public Instant GeneralEntryOpensAt { get; set; } = Instant.MinValue;

    /// <summary>
    /// Display-only age threshold under which the photo-ID step may be waived for
    /// a minor accompanied by a named adult. The gate agent makes the call; this
    /// is the number shown to them, not an age computed from a date of birth.
    /// </summary>
    public int MinorAgeThresholdYears { get; set; } = 16;
}
