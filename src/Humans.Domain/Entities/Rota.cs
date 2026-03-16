using Humans.Domain.Enums;
using NodaTime;

namespace Humans.Domain.Entities;

/// <summary>
/// A shift container belonging to a department (parent team) and event.
/// Groups related shifts under a named rota with shared priority and signup policy.
/// </summary>
public class Rota
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// FK to the event configuration this rota belongs to.
    /// </summary>
    public Guid EventSettingsId { get; set; }

    /// <summary>
    /// FK to the department (parent team) this rota belongs to.
    /// </summary>
    public Guid TeamId { get; set; }

    /// <summary>
    /// Display name for the rota (e.g., "Gate Shifts", "Bar Cleanup").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this rota covers.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Priority level affecting urgency scoring.
    /// </summary>
    public ShiftPriority Priority { get; set; }

    /// <summary>
    /// Whether signups are auto-confirmed or require coordinator approval.
    /// </summary>
    public SignupPolicy Policy { get; set; }

    /// <summary>
    /// Whether this rota is currently active and accepting signups.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this rota was created.
    /// </summary>
    public Instant CreatedAt { get; init; }

    /// <summary>
    /// When this rota was last updated.
    /// </summary>
    public Instant UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property to the event configuration.
    /// </summary>
    public EventSettings EventSettings { get; set; } = null!;

    /// <summary>
    /// Navigation property to the department (parent team).
    /// </summary>
    public Team Team { get; set; } = null!;

    /// <summary>
    /// Navigation property to shifts within this rota.
    /// </summary>
    public ICollection<Shift> Shifts { get; } = new List<Shift>();
}
