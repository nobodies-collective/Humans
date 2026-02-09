namespace Profiles.Domain.Enums;

/// <summary>
/// Actions recorded in the Google sync audit log.
/// Stored as string in DB; new values can be appended without migration.
/// </summary>
public enum GoogleSyncAction
{
    PermissionGranted,
    PermissionRevoked,
    MemberAdded,
    MemberRemoved
}
