using NodaTime;
using Profiles.Domain.Enums;

namespace Profiles.Domain.Entities;

/// <summary>
/// Immutable record of a Google resource permission change (grant/revoke).
/// This table is append-only — no updates or deletes allowed.
/// </summary>
public class GoogleSyncAuditEntry
{
    /// <summary>
    /// Unique identifier for the audit entry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Foreign key to the affected Google resource.
    /// </summary>
    public Guid ResourceId { get; init; }

    /// <summary>
    /// Navigation property to the affected Google resource.
    /// Uses set (not init) as required by EF Core for navigation properties.
    /// </summary>
    public GoogleResource? Resource { get; set; }

    /// <summary>
    /// Foreign key to the user whose access changed (nullable for non-user actions).
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Navigation property to the user.
    /// Uses set (not init) as required by EF Core for navigation properties.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Email address at the time of the action.
    /// Denormalized for history — preserved even if user is later anonymized.
    /// </summary>
    public string UserEmail { get; init; } = string.Empty;

    /// <summary>
    /// What happened (PermissionGranted, PermissionRevoked, MemberAdded, MemberRemoved).
    /// </summary>
    public GoogleSyncAction Action { get; init; }

    /// <summary>
    /// The role granted or revoked (e.g., "writer", "fileOrganizer", "MEMBER").
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// What triggered this action.
    /// </summary>
    public GoogleSyncSource Source { get; init; }

    /// <summary>
    /// When the action occurred.
    /// </summary>
    public Instant Timestamp { get; init; }

    /// <summary>
    /// Whether the Google API call succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error details if the API call failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
