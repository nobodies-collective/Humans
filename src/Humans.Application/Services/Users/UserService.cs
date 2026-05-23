using System.ComponentModel.DataAnnotations;
using Humans.Application.DTOs;
using Humans.Application.Extensions;
using Humans.Application.Interfaces.Auth;
using Humans.Application.Interfaces.Gdpr;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Interfaces.Users;
using Humans.Application.Services.Profiles;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Domain.Helpers;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Humans.Application.Services.Users;

public sealed class UserService(
    IUserRepository repo,
    IUserEmailRepository userEmailRepo,
    IProfileRepository profileRepo,
    IContactFieldRepository contactFieldRepo,
    ICommunicationPreferenceRepository communicationPreferenceRepo,
    IAdminAuthorizationService adminAuthorization,
    IClock clock,
    ILogger<UserService> logger) : IUserService, IUserDataContributor, IUserMerge
{
    // --- User reads ---

    public async ValueTask<UserInfo?> GetUserInfoAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await repo.GetByIdAsync(userId, ct);
        if (user is null) return null;

        var userEmails = await userEmailRepo.GetByUserIdReadOnlyAsync(userId, ct);
        var participations = await repo.GetEventParticipationsByUserIdAsync(userId, ct);
        var externalLoginsMap = await repo.GetExternalLoginsByUserIdsAsync([userId], ct);
        var externalLogins = externalLoginsMap.TryGetValue(userId, out var logins)
            ? logins
            : [];

        var profile = await profileRepo.GetByUserIdReadOnlyAsync(userId, ct);
        IReadOnlyList<ContactField> contactFields = [];
        IReadOnlyList<ProfileLanguage> languages = [];
        IReadOnlyList<VolunteerHistoryEntry> volunteerHistory = [];
        if (profile is not null)
        {
            contactFields = await contactFieldRepo.GetByProfileIdReadOnlyAsync(profile.Id, ct);
            languages = profile.Languages.ToList();
            volunteerHistory = profile.VolunteerHistory.ToList();
        }

        var communicationPreferences = await communicationPreferenceRepo
            .GetByUserIdReadOnlyAsync(userId, ct);

        return UserInfo.Create(
            user, userEmails, participations, externalLogins,
            profile, contactFields, languages, volunteerHistory,
            communicationPreferences);
    }

    public Task<IReadOnlyCollection<UserInfo>> GetAllUserInfosAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(
            "GetAllUserInfosAsync is only meaningful through CachingUserService. " +
            "If this is being called on the inner UserService it indicates a DI " +
            "registration mistake — IUserService should resolve to CachingUserService.");

    public ValueTask<IReadOnlyDictionary<Guid, UserInfo>> GetUserInfosAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "GetUserInfosAsync is only meaningful through CachingUserService — " +
            "the decorator serves cached hits from the dict and refills misses " +
            "through inner.GetUserInfoAsync. If this is being called on the inner " +
            "UserService it indicates a DI registration mistake.");

    public Task<IReadOnlyList<HumanSearchResult>> SearchUsersAsync(
        string query, PersonSearchFields fields, int limit = 10, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "SearchUsersAsync runs against the cached UserInfo snapshot in " +
            "CachingUserService. If this is being called on the inner UserService " +
            "it indicates a DI registration mistake — IUserService should resolve " +
            "to CachingUserService.");

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default) =>
        repo.GetByIdAsync(userId, ct);

    public Task<IReadOnlyDictionary<Guid, User>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        repo.GetByIdsAsync(userIds, ct);

    public Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken ct = default) =>
        repo.GetAllAsync(ct);

    public async Task<string?> PurgeOwnDataAsync(Guid userId, CancellationToken ct = default)
    {
        var displayName = await repo.PurgeAsync(userId, ct);
        if (displayName is null)
            return null;

        logger.LogWarning("Purged human {DisplayName} ({HumanId})", displayName, userId);

        return displayName;
    }

    public async Task<ExpiredDeletionAnonymizationResult?> ApplyExpiredDeletionAnonymizationAsync(
        Guid userId, CancellationToken ct = default)
    {
        // Own-data only — cross-section cascade lives in IAccountDeletionService.
        return await repo.ApplyExpiredDeletionAnonymizationAsync(userId, ct);
    }

    public Task<User?> GetByEmailOrAlternateAsync(string email, CancellationToken ct = default)
    {
        var normalized = EmailNormalization.NormalizeForComparison(email);
        var alternate = GetAlternateEmail(normalized);
        return repo.GetByEmailOrAlternateAsync(normalized, alternate, ct);
    }

    [Obsolete("Issue nobodies-collective/Humans#687: User.GoogleEmail is being deprecated. Use IUserEmailService.GetOtherUserIdHavingEmailAsync.")]
    public Task<Guid?> GetOtherUserIdHavingGoogleEmailAsync(
        string email, Guid excludeUserId, CancellationToken ct = default) =>
        repo.GetOtherUserIdHavingGoogleEmailAsync(email, excludeUserId, ct);

    public Task<IReadOnlyList<Guid>> GetAccountsDueForAnonymizationAsync(
        Instant now, CancellationToken ct = default) =>
        repo.GetAccountsDueForAnonymizationAsync(now, ct);

    // --- User writes ---

    public async Task<bool> TrySetGoogleEmailStatusFromSyncAsync(
        Guid userId, GoogleEmailStatus status, CancellationToken ct = default)
    {
        if (status == GoogleEmailStatus.Valid)
        {
            var user = await repo.GetByIdAsync(userId, ct);
            if (user is null || user.GoogleEmailStatus == GoogleEmailStatus.Rejected)
                return false;
        }

        return await SetGoogleEmailStatusInternalAsync(userId, status, ct);
    }

    // Private write — Try variant is the only external entry point (sync-driven; Rejected is terminal).
    private async Task<bool> SetGoogleEmailStatusInternalAsync(
        Guid userId, GoogleEmailStatus status, CancellationToken ct = default)
    {
        return await repo.SetGoogleEmailStatusAsync(userId, status, ct);
    }

    public async Task UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default)
    {
        await repo.UpdateDisplayNameAsync(userId, displayName, ct);
    }

    public async Task SetPreferredLanguageAsync(Guid userId, string preferredLanguage, CancellationToken ct = default)
    {
        await repo.SetPreferredLanguageAsync(userId, preferredLanguage, ct);
    }

    public async Task SetICalTokenAsync(Guid userId, Guid token, CancellationToken ct = default)
    {
        await repo.SetICalTokenAsync(userId, token, ct);
    }

    public async Task<bool> SetDeletionPendingAsync(
        Guid userId, Instant requestedAt, Instant scheduledFor, Instant? eligibleAfter,
        CancellationToken ct = default)
    {
        var updated = await repo.SetDeletionPendingAsync(
            userId, requestedAt, scheduledFor, eligibleAfter, ct);
        return updated;
    }

    public async Task<bool> ClearDeletionAsync(Guid userId, CancellationToken ct = default)
    {
        return await repo.ClearDeletionAsync(userId, ct);
    }

    public async Task<UserEmailAddResult> AddUserEmailAsync(
        Guid userId,
        UserEmailAddCommand command,
        CancellationToken ct = default)
    {
        var email = command.Email.Trim();
        var normalizedEmail = EmailNormalization.NormalizeForComparison(email);
        var alternateEmail = GetAlternateEmail(normalizedEmail);

        if (!new EmailAddressAttribute().IsValid(email))
            throw new ValidationException("Please enter a valid email address.");

        var existing = await userEmailRepo.FindByNormalizedEmailAsync(normalizedEmail, alternateEmail, ct);
        if (existing is not null && existing.UserId == userId)
        {
            if (command.IgnoreExisting)
                return new UserEmailAddResult(existing.Id, Added: false, IsConflict: false);

            throw new ValidationException("This email address is already in your account.");
        }

        var isConflict = existing is not null && existing.UserId != userId && existing.IsVerified;

        var user = await repo.GetByIdAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var primaryEmail = await GetPrimaryEmailFromRowsAsync(user, userId, ct);
        if (EmailNormalization.EmailsMatch(email, primaryEmail))
            throw new ValidationException("This is already your sign-in email.");

        var now = clock.GetCurrentInstant();
        var row = new UserEmail
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email,
            IsVerified = command.IsVerified,
            IsPrimary = false,
            IsGoogle = false,
            Visibility = command.Visibility,
            Provider = command.Provider,
            ProviderKey = command.ProviderKey,
            VerificationSentAt = command.VerificationSentAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AddRowWithInvariantsAsync(row, ct);
        return new UserEmailAddResult(row.Id, Added: true, isConflict);
    }

    public async Task<bool> UpdateUserEmailAsync(
        Guid userId,
        Guid emailId,
        UserEmailUpdateCommand command,
        CancellationToken ct = default)
    {
        var changed = false;

        if (command.MarkVerified)
        {
            var email = await userEmailRepo.GetByIdAndUserIdAsync(emailId, userId, ct)
                ?? throw new InvalidOperationException("Email not found.");

            if (email.IsVerified || email.Provider is not null)
                throw new ValidationException("No email pending verification.");

            email.IsVerified = true;
            email.UpdatedAt = clock.GetCurrentInstant();
            await userEmailRepo.UpdateAsync(email, ct);
            await EnsurePrimaryInvariantAsync(userId, ct);
            await EnsureGoogleInvariantAsync(userId, ct);
            changed = true;
        }

        if (command.Primary == UserEmailPrimaryChange.MakePrimary)
        {
            await SetPrimaryEmailAsync(userId, emailId, ct);
            changed = true;
        }
        else if (command.Primary == UserEmailPrimaryChange.ClearDuplicatePrimary)
        {
            changed |= await ClearDuplicatePrimaryAsync(userId, emailId, ct);
        }

        if (command.Google == UserEmailGoogleChange.MakeGoogle)
        {
            var row = await userEmailRepo.GetByIdAndUserIdAsync(emailId, userId, ct);
            if (row is null || !row.IsVerified) return false;
            await userEmailRepo.SetGoogleExclusiveAsync(userId, emailId, clock.GetCurrentInstant(), ct);
            changed = true;
        }
        else if (command.Google == UserEmailGoogleChange.ClearDuplicateGoogle)
        {
            changed |= await ClearDuplicateGoogleAsync(userId, emailId, ct);
        }

        if (command.ChangeVisibility)
        {
            var row = await userEmailRepo.GetByIdAndUserIdAsync(emailId, userId, ct)
                ?? throw new InvalidOperationException("Email not found.");
            row.Visibility = command.Visibility;
            row.UpdatedAt = clock.GetCurrentInstant();
            await userEmailRepo.UpdateAsync(row, ct);
            changed = true;
        }

        return changed;
    }

    public async Task<bool> RemoveUserEmailAsync(
        Guid userId,
        Guid emailId,
        UserEmailRemoveCommand command,
        CancellationToken ct = default)
    {
        var email = await userEmailRepo.GetByIdAndUserIdAsync(emailId, userId, ct)
            ?? throw new InvalidOperationException("Email not found.");

        var hasProvider = !string.IsNullOrEmpty(email.Provider);
        if (command.Mode == UserEmailRemovalMode.PlainEmail && hasProvider)
            return false;
        if (command.Mode == UserEmailRemovalMode.ProviderLinkedEmail && !hasProvider)
            return false;

        if (command.PreserveLastVerifiedEmail && email.IsVerified)
        {
            var allEmails = await userEmailRepo.GetByUserIdForMutationAsync(userId, ct);
            var verifiedRemaining = allEmails.Count(e => e.IsVerified && e.Id != emailId);

            if (verifiedRemaining == 0)
            {
                throw new ValidationException(
                    "Cannot remove your last verified email. Add another verified email first " +
                    "so you can still receive system notifications.");
            }
        }

        await userEmailRepo.RemoveAsync(email, ct);

        if (command.RepairInvariants)
        {
            await EnsurePrimaryInvariantAsync(userId, ct);
            await EnsureGoogleInvariantAsync(userId, ct);
        }

        return true;
    }

    public Task SetLastConsentReminderSentAsync(
        Guid userId, Instant sentAt, CancellationToken ct = default) =>
        repo.SetLastConsentReminderSentAsync(userId, sentAt, ct);

    // --- EventParticipation reads ---

    public Task<List<EventParticipation>> GetAllParticipationsForYearAsync(int year, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "GetAllParticipationsForYearAsync is only meaningful through CachingUserService — " +
            "projects the year's participations from the cached UserInfo snapshot. If this is " +
            "being called on the inner UserService it indicates a DI registration mistake.");

    // --- EventParticipation writes ---

    public async Task<EventParticipation> DeclareNotAttendingAsync(Guid userId, int year, CancellationToken ct = default)
    {
        var now = clock.GetCurrentInstant();
        var persisted = await repo.UpsertParticipationAsync(
            userId, year, ParticipationStatus.NotAttending, ParticipationSource.UserDeclared,
            declaredAt: now, checkedInAt: null, ct);

        if (persisted is null)
        {
            // Blocked by Attended.
            logger.LogWarning(
                "Cannot declare NotAttending for user {UserId} year {Year} — already Attended",
                userId, year);
            // Re-read because upsert returned null.
            return (await repo.GetParticipationAsync(userId, year, ct))!;
        }

        logger.LogInformation(
            "User {UserId} declared NotAttending for year {Year}",
            userId, year);
        return persisted;
    }

    public async Task<bool> UndoNotAttendingAsync(Guid userId, int year, CancellationToken ct = default)
    {
        var existing = await repo.GetParticipationAsync(userId, year, ct);
        if (existing is null)
            return false;

        if (existing.Status != ParticipationStatus.NotAttending ||
            existing.Source != ParticipationSource.UserDeclared)
        {
            logger.LogWarning(
                "Cannot undo NotAttending for user {UserId} year {Year} — status is {Status} from {Source}",
                userId, year, existing.Status, existing.Source);
            return false;
        }

        var removed = await repo.RemoveParticipationAsync(
            userId, year, ParticipationSource.UserDeclared, ct);

        if (removed)
        {
            logger.LogInformation(
                "User {UserId} undid NotAttending declaration for year {Year}",
                userId, year);
        }

        return removed;
    }

    public Task SetParticipationFromTicketSyncAsync(
        Guid userId, int year, ParticipationStatus status, Instant? checkedInAt, CancellationToken ct = default) =>
        // Attended-is-permanent, source-override and CheckedInAt-never-overwrite
        // semantics live in repo upsert.
        repo.UpsertParticipationAsync(
            userId, year, status, ParticipationSource.TicketSync,
            declaredAt: null, checkedInAt: checkedInAt, ct);

    public Task<IReadOnlyList<OnsiteUserRow>> GetOnsiteUsersAsync(int year, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "GetOnsiteUsersAsync is only meaningful through CachingUserService — it projects " +
            "Attended+CheckedInAt rows from the cached UserInfo snapshot. Hitting the inner " +
            "UserService indicates a DI registration mistake.");

    public Task RemoveTicketSyncParticipationAsync(Guid userId, int year, CancellationToken ct = default) =>
        repo.RemoveParticipationAsync(userId, year, ParticipationSource.TicketSync, ct);

    public async Task<int> BackfillParticipationsAsync(
        int year,
        List<(Guid UserId, ParticipationStatus Status)> entries,
        CancellationToken ct = default)
    {
        var count = await repo.BackfillParticipationsAsync(year, entries, ct);
        logger.LogInformation(
            "Backfilled {Count} participation records for year {Year}",
            count, year);
        return count;
    }

    // --- IUserDataContributor — GDPR export ---

    public async Task<IReadOnlyList<UserDataSlice>> ContributeForUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await repo.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return [new UserDataSlice(GdprExportSections.Account, null)];
        }

        // see nobodies-collective/Humans#687 — User.GoogleEmail deprecated, not exported.
        var userEmails = await userEmailRepo.GetByUserIdReadOnlyAsync(userId, ct);
        var googleEmail = userEmails
            .Where(e => e.IsVerified && e.IsGoogle)
            .Select(e => e.Email)
            .FirstOrDefault();

        var shaped = new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.PreferredLanguage,
            GoogleEmail = googleEmail,
            user.UnsubscribedFromCampaigns,
            user.SuppressScheduleChangeEmails,
            ContactSource = user.ContactSource?.ToString(),
            DeletionRequestedAt = user.DeletionRequestedAt.ToInvariantInstantString(),
            DeletionScheduledFor = user.DeletionScheduledFor.ToInvariantInstantString(),
            CreatedAt = user.CreatedAt.ToInvariantInstantString(),
            LastLoginAt = user.LastLoginAt.ToInvariantInstantString()
        };

        var participations = await repo.GetEventParticipationsByUserIdAsync(userId, ct);
        var participationsShaped = participations
            .Select(ep => new
            {
                ep.Year,
                Status = ep.Status.ToString(),
                Source = ep.Source.ToString(),
                DeclaredAt = ep.DeclaredAt.ToInvariantInstantString()
            })
            .ToList();

        return
        [
            new UserDataSlice(GdprExportSections.Account, shaped),
            new UserDataSlice(GdprExportSections.EventParticipations, participationsShaped)
        ];
    }

    private async Task<string?> GetPrimaryEmailFromRowsAsync(User user, Guid userId, CancellationToken ct)
    {
        var emails = await userEmailRepo.GetByUserIdReadOnlyAsync(userId, ct);
        return emails.FirstOrDefault(e => e.IsVerified && e.IsPrimary)?.Email
            ?? emails.FirstOrDefault(e => e.IsVerified)?.Email
            ?? user.IdentityEmailColumn;
    }

    private async Task SetPrimaryEmailAsync(Guid userId, Guid emailId, CancellationToken ct)
    {
        var emails = (await userEmailRepo.GetByUserIdForMutationAsync(userId, ct)).ToList();

        var target = emails.FirstOrDefault(e => e.Id == emailId)
            ?? throw new InvalidOperationException("Email not found.");

        if (!target.IsVerified)
            throw new ValidationException("Only verified emails can be the notification target.");

        var now = clock.GetCurrentInstant();
        var changed = new List<UserEmail>();
        foreach (var email in emails)
        {
            var shouldBePrimary = email.Id == emailId;
            if (email.IsPrimary != shouldBePrimary)
            {
                email.IsPrimary = shouldBePrimary;
                email.UpdatedAt = now;
                changed.Add(email);
            }
        }

        if (changed.Count > 0)
            await userEmailRepo.UpdateBatchAsync(changed, ct);
    }

    private async Task<bool> ClearDuplicatePrimaryAsync(Guid userId, Guid emailId, CancellationToken ct)
    {
        var row = await userEmailRepo.GetByIdAndUserIdAsync(emailId, userId, ct);
        if (row is null || !row.IsPrimary) return false;

        var allEmails = await userEmailRepo.GetByUserIdReadOnlyAsync(userId, ct);
        if (allEmails.Count(e => e.IsPrimary && e.IsVerified) <= 1) return false;

        row.IsPrimary = false;
        row.UpdatedAt = clock.GetCurrentInstant();
        await userEmailRepo.UpdateAsync(row, ct);
        return true;
    }

    private async Task<bool> ClearDuplicateGoogleAsync(Guid userId, Guid emailId, CancellationToken ct)
    {
        var row = await userEmailRepo.GetByIdAndUserIdAsync(emailId, userId, ct);
        if (row is null || !row.IsGoogle) return false;

        var allEmails = await userEmailRepo.GetByUserIdReadOnlyAsync(userId, ct);
        if (allEmails.Count(e => e.IsGoogle) <= 1) return false;

        row.IsGoogle = false;
        row.UpdatedAt = clock.GetCurrentInstant();
        await userEmailRepo.UpdateAsync(row, ct);
        return true;
    }

    private async Task EnsurePrimaryInvariantAsync(Guid userId, CancellationToken ct)
    {
        var emails = (await userEmailRepo.GetByUserIdForMutationAsync(userId, ct)).ToList();
        var verified = emails.Where(e => e.IsVerified).ToList();
        if (verified.Count == 0)
            return;

        var currentPrimaries = verified.Where(e => e.IsPrimary).ToList();

        var winner =
            verified.FirstOrDefault(e => e.Email.EndsWith("@nobodies.team", StringComparison.OrdinalIgnoreCase))
            ?? currentPrimaries.OrderBy(e => e.Id).FirstOrDefault()
            ?? verified.OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Id).First();

        if (currentPrimaries.Count == 1 && currentPrimaries[0].Id == winner.Id)
            return;

        var now = clock.GetCurrentInstant();
        var changed = new List<UserEmail>();
        foreach (var row in verified)
        {
            var shouldBePrimary = row.Id == winner.Id;
            if (row.IsPrimary != shouldBePrimary)
            {
                row.IsPrimary = shouldBePrimary;
                row.UpdatedAt = now;
                changed.Add(row);
            }
        }

        if (changed.Count > 0)
            await userEmailRepo.UpdateBatchAsync(changed, ct);
    }

    private async Task EnsureGoogleInvariantAsync(Guid userId, CancellationToken ct)
    {
        var emails = (await userEmailRepo.GetByUserIdForMutationAsync(userId, ct)).ToList();
        var verified = emails.Where(e => e.IsVerified).ToList();
        if (verified.Count == 0)
            return;

        var currentGoogles = verified.Where(e => e.IsGoogle).ToList();

        var winner =
            verified.FirstOrDefault(e => e.Email.EndsWith("@nobodies.team", StringComparison.OrdinalIgnoreCase))
            ?? currentGoogles.OrderBy(e => e.Id).FirstOrDefault()
            ?? verified.OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Id).First();

        if (currentGoogles.Count == 1 && currentGoogles[0].Id == winner.Id)
            return;

        var now = clock.GetCurrentInstant();
        var changed = new List<UserEmail>();
        foreach (var row in verified)
        {
            var shouldBeGoogle = row.Id == winner.Id;
            if (row.IsGoogle != shouldBeGoogle)
            {
                row.IsGoogle = shouldBeGoogle;
                row.UpdatedAt = now;
                changed.Add(row);
            }
        }

        if (changed.Count > 0)
            await userEmailRepo.UpdateBatchAsync(changed, ct);
    }

    private async Task AddRowWithInvariantsAsync(UserEmail row, CancellationToken ct)
    {
        await userEmailRepo.AddAsync(row, ct);
        await EnsurePrimaryInvariantAsync(row.UserId, ct);
        await EnsureGoogleInvariantAsync(row.UserId, ct);
    }

    private static string? GetAlternateEmail(string normalizedEmail)
    {
        if (normalizedEmail.EndsWith("@gmail.com", StringComparison.Ordinal))
            return $"{normalizedEmail[..^"@gmail.com".Length]}@googlemail.com";

        if (normalizedEmail.EndsWith("@googlemail.com", StringComparison.Ordinal))
            return $"{normalizedEmail[..^"@googlemail.com".Length]}@gmail.com";

        return null;
    }

    // --- Account merge: fold-into-target primitives ---

    public async Task<bool> AnonymizeForMergeAsync(
        Guid sourceUserId, Guid targetUserId, Instant now,
        CancellationToken ct = default)
    {
        return await repo.AnonymizeForMergeAsync(sourceUserId, targetUserId, now, ct);
    }

    public async Task ReassignAsync(Guid mergedFromUserId, Guid mergedToUserId, Guid actorUserId, Instant now,
        CancellationToken ct)
    {
        // `now` and `actorUserId` unused — kept for IUserMerge contract.
        _ = actorUserId;
        _ = now;
        await repo.ReassignLoginsToUserAsync(mergedFromUserId, mergedToUserId, ct);
        await repo.ReassignEventParticipationToUserAsync(mergedFromUserId, mergedToUserId, ct);
    }

    public Task<IReadOnlySet<Guid>> GetMergedSourceIdsAsync(
        Guid targetUserId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "GetMergedSourceIdsAsync is only meaningful through CachingUserService — " +
            "scans the cached UserInfo snapshot for MergedToUserId tombstones. If this is " +
            "being called on the inner UserService it indicates a DI registration mistake.");

    public Task<IReadOnlyList<Guid>> GetUsersWithLoginsButNoEmailsAsync(CancellationToken ct = default) =>
        repo.GetUsersWithLoginsButNoEmailsAsync(ct);

    public async Task<int> DeleteUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default)
    {
        await adminAuthorization.RequireCurrentUserIsAdminAsync(ct);
        return await repo.DeleteUsersAsync(userIds, ct);
    }

    public Task<int> DeleteAllExternalLoginsForUserAsync(Guid userId, CancellationToken ct = default) =>
        repo.DeleteAllExternalLoginsForUserAsync(userId, ct);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<(string Provider, string ProviderKey)>>>
        GetExternalLoginsByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        repo.GetExternalLoginsByUserIdsAsync(userIds, ct);
}
