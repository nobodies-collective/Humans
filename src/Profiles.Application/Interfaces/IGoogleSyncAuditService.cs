using Profiles.Domain.Entities;
using Profiles.Domain.Enums;

namespace Profiles.Application.Interfaces;

/// <summary>
/// Service for recording Google sync audit entries.
/// Entries are added to the DbContext but NOT saved — the caller's SaveChangesAsync
/// persists them atomically with the business operation.
/// </summary>
public interface IGoogleSyncAuditService
{
    /// <summary>
    /// Records a Google sync audit entry.
    /// </summary>
    Task LogAsync(
        Guid resourceId,
        Guid? userId,
        string userEmail,
        GoogleSyncAction action,
        string role,
        GoogleSyncSource source,
        bool success,
        string? errorMessage = null);

    /// <summary>
    /// Gets audit entries for a specific Google resource.
    /// </summary>
    Task<IReadOnlyList<GoogleSyncAuditEntry>> GetByResourceAsync(Guid resourceId);

    /// <summary>
    /// Gets audit entries for a specific user.
    /// </summary>
    Task<IReadOnlyList<GoogleSyncAuditEntry>> GetByUserAsync(Guid userId);
}
