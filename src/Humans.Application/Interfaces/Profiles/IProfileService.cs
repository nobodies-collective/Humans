using Humans.Application.DTOs;
using Humans.Application.Interfaces.Users;
using Humans.Domain.Entities;

namespace Humans.Application.Interfaces.Profiles;

/// <summary>
/// Service for managing profile data and profile-side merge fan-out.
/// </summary>
/// <remarks>
/// SurfaceBudget intentionally removed for the duration of the Users+Profile
/// section merge — the interface is shrinking as its surface migrates onto
/// IUserService over the next several PRs and is slated for deletion when the
/// merge completes. Per-PR budget churn is not useful while that is in flight.
/// </remarks>
public interface IProfileService : IApplicationService, IUserMerge
{
    /// <summary>
    /// Returns the profile picture for the given profile, read from the
    /// filesystem store. The DB <c>ProfilePictureContentType</c> column is
    /// consulted first as a GDPR gate — if null (no picture, or anonymized)
    /// the call returns <c>null</c> without touching disk so a stale on-disk
    /// file left behind by a failed anonymization cleanup is not served.
    /// Returns <c>null</c> when the file is missing on disk. Centralizing
    /// the read path here keeps controllers free of <see cref="IFileStorage"/>.
    /// </summary>
    Task<(byte[] Data, string ContentType)?> GetProfilePictureAsync(Guid profileId, CancellationToken ct = default);

    /// <summary>
    /// Issue nobodies-collective/Humans#702: snapshot for the profile-picture
    /// DB→FS migration verification page. Returns the total count of profiles
    /// with a custom picture (<c>ProfilePictureContentType IS NOT NULL</c>),
    /// how many of those have the expected file on disk, and the DB-only rows
    /// (the at-risk laggards that need to be migrated before Phase 2 drops
    /// <c>Profile.ProfilePictureData</c>). FS existence is checked with
    /// <see cref="IFileStorage.TryReadAsync"/> against the same key the read
    /// path uses; the FS-key helper stays section-internal.
    /// </summary>
    Task<ProfilePictureMigrationSnapshot> GetProfilePictureMigrationSnapshotAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Persists a new custom profile picture for the user's profile. No-op (logs a
    /// warning) if the user has no profile yet. Invalidates the UserInfo cache
    /// entry via the caching decorator. Callers are responsible for validating and
    /// resizing the image before calling.
    /// </summary>
    Task SetProfilePictureAsync(Guid userId, byte[] pictureData, string contentType, CancellationToken ct = default);
    Task<Guid> SaveProfileAsync(Guid userId, string displayName, ProfileSaveRequest request, string language, CancellationToken ct = default);
    Task<(bool CanAdd, int MinutesUntilResend, Guid? PendingEmailId)>
        GetEmailCooldownInfoAsync(Guid pendingEmailId, CancellationToken ct = default);

    /// <summary>
    /// Anonymizes the personal fields of the user's profile for GDPR
    /// expiry-based deletion: clears first/last name → "Deleted"/"User",
    /// burner name → empty, and blanks every optional demographic /
    /// emergency-contact / admin-note / contribution-interest field. Also
    /// removes every <c>ContactField</c> and <c>VolunteerHistoryEntry</c> row
    /// owned by the profile in the same save. No-op if the user has no
    /// profile. Returns <c>true</c> if a profile was anonymized. Used by the
    /// account deletion job via
    /// <see cref="Users.IAccountDeletionService.AnonymizeExpiredAccountAsync"/>.
    /// </summary>
    Task<bool> AnonymizeExpiredProfileAsync(Guid userId, CancellationToken ct = default);

}
