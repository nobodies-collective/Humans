using Humans.Domain.Entities;
using Humans.Domain.Enums;
using NodaTime;
using MemberApplication = Humans.Domain.Entities.Application;

namespace Humans.Application.Interfaces;

public record ProfileSaveRequest(
    string BurnerName, string FirstName, string LastName,
    string? City, string? CountryCode, double? Latitude, double? Longitude, string? PlaceId,
    string? Bio, string? Pronouns, string? ContributionInterests, string? BoardNotes,
    int? BirthdayMonth, int? BirthdayDay,
    string? EmergencyContactName, string? EmergencyContactPhone, string? EmergencyContactRelationship,
    bool NoPriorBurnExperience,
    byte[]? ProfilePictureData, string? ProfilePictureContentType, bool RemoveProfilePicture,
    MembershipTier? SelectedTier, string? ApplicationMotivation, string? ApplicationAdditionalInfo,
    string? ApplicationSignificantContribution, string? ApplicationRoleUnderstanding);

public record CachedProfile(
    Guid UserId, string DisplayName, string? ProfilePictureUrl,
    bool HasCustomPicture, Guid ProfileId, long UpdatedAtTicks,
    string? BurnerName, string? Bio, string? Pronouns,
    string? ContributionInterests,
    string? City, string? CountryCode, double? Latitude, double? Longitude,
    int? BirthdayDay, int? BirthdayMonth,
    IReadOnlyList<CachedVolunteerEntry> VolunteerHistory)
{
    public static CachedProfile Create(Profile profile, User user) => new(
        UserId: user.Id,
        DisplayName: user.DisplayName,
        ProfilePictureUrl: user.ProfilePictureUrl,
        HasCustomPicture: profile.ProfilePictureData != null,
        ProfileId: profile.Id,
        UpdatedAtTicks: profile.UpdatedAt.ToUnixTimeTicks(),
        BurnerName: profile.BurnerName,
        Bio: profile.Bio,
        Pronouns: profile.Pronouns,
        ContributionInterests: profile.ContributionInterests,
        City: profile.City,
        CountryCode: profile.CountryCode,
        Latitude: profile.Latitude,
        Longitude: profile.Longitude,
        BirthdayDay: profile.DateOfBirth?.Day,
        BirthdayMonth: profile.DateOfBirth?.Month,
        VolunteerHistory: profile.VolunteerHistory
            .Select(v => new CachedVolunteerEntry(v.EventName, v.Description))
            .ToList());
}

public record CachedVolunteerEntry(string EventName, string? Description);

public interface IProfileService
{
    Task<Profile?> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<(Profile? Profile, MemberApplication? LatestApplication, int PendingConsentCount)>
        GetProfileIndexDataAsync(Guid userId, CancellationToken ct = default);
    Task<(Profile? Profile, bool IsTierLocked, MemberApplication? PendingApplication)>
        GetProfileEditDataAsync(Guid userId, CancellationToken ct = default);
    Task<(byte[]? Data, string? ContentType)> GetProfilePictureAsync(Guid profileId, CancellationToken ct = default);
    Task<Guid> SaveProfileAsync(Guid userId, string displayName, ProfileSaveRequest request, string language, CancellationToken ct = default);
    Task<OnboardingResult> RequestDeletionAsync(Guid userId, CancellationToken ct = default);
    Task<OnboardingResult> CancelDeletionAsync(Guid userId, CancellationToken ct = default);
    Task<object> ExportDataAsync(Guid userId, CancellationToken ct = default);
    Task<(bool CanAdd, int MinutesUntilResend, Guid? PendingEmailId)>
        GetEmailCooldownInfoAsync(Guid pendingEmailId, CancellationToken ct = default);

    Task<(int ColaboradorCount, int AsociadoCount)> GetTierCountsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<(Guid ProfileId, Guid UserId, long UpdatedAtTicks)>>
        GetCustomPictureInfoByUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid UserId, string DisplayName, string? ProfilePictureUrl, bool HasCustomPicture, Guid ProfileId, int Day, int Month)>>
        GetBirthdayProfilesAsync(int month, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid UserId, string DisplayName, string? ProfilePictureUrl, double Latitude, double Longitude, string? City, string? CountryCode)>>
        GetApprovedProfilesWithLocationAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DTOs.AdminHumanRow>> GetFilteredHumansAsync(
        string? search, string? statusFilter, CancellationToken ct = default);

    Task<DTOs.AdminHumanDetailData?> GetAdminHumanDetailAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserSearchResult>> SearchApprovedUsersAsync(string query, CancellationToken ct = default);

    Task<IReadOnlyList<HumanSearchResult>> SearchHumansAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Updates a single entry in the approved profiles cache.
    /// Pass null to remove the entry (e.g., on suspension/deletion).
    /// </summary>
    void UpdateProfileCache(Guid userId, CachedProfile? newValue);

    /// <summary>
    /// Gets or creates a volunteer event profile for the user in the given event.
    /// </summary>
    Task<VolunteerEventProfile> GetOrCreateEventProfileAsync(Guid userId, Guid eventSettingsId);

    /// <summary>
    /// Updates a volunteer event profile.
    /// </summary>
    Task UpdateEventProfileAsync(VolunteerEventProfile profile);

    /// <summary>
    /// Gets a user's volunteer event profile. Medical data included only when includeMedical=true.
    /// </summary>
    Task<VolunteerEventProfile?> GetEventProfileAsync(Guid userId, Guid eventSettingsId, bool includeMedical);
}

public record UserSearchResult(Guid UserId, string DisplayName, string Email);

public record HumanSearchResult(
    Guid UserId,
    string DisplayName,
    string? BurnerName,
    string? City,
    string? Bio,
    string? ContributionInterests,
    string? ProfilePictureUrl,
    bool HasCustomPicture,
    Guid ProfileId,
    long UpdatedAtTicks,
    string? MatchField,
    string? MatchSnippet);
