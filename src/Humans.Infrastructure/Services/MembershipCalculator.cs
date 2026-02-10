using Microsoft.EntityFrameworkCore;
using NodaTime;
using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;

namespace Humans.Infrastructure.Services;

/// <summary>
/// Service for computing membership status.
/// </summary>
public class MembershipCalculator : IMembershipCalculator
{
    private readonly HumansDbContext _dbContext;
    private readonly IConsentRecordRepository _consentRepository;
    private readonly IClock _clock;

    public MembershipCalculator(
        HumansDbContext dbContext,
        IConsentRecordRepository consentRepository,
        IClock clock)
    {
        _dbContext = dbContext;
        _consentRepository = consentRepository;
        _clock = clock;
    }

    public async Task<MembershipStatus> ComputeStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile == null)
        {
            return MembershipStatus.None;
        }

        if (profile.IsSuspended)
        {
            return MembershipStatus.Suspended;
        }

        var hasActiveRoles = await HasActiveRolesAsync(userId, cancellationToken);
        if (!hasActiveRoles)
        {
            return MembershipStatus.None;
        }

        var hasExpiredConsents = await HasAnyExpiredConsentsForTeamAsync(userId, SystemTeamIds.Volunteers, cancellationToken);
        if (hasExpiredConsents)
        {
            return MembershipStatus.Inactive;
        }

        return MembershipStatus.Active;
    }

    public async Task<bool> HasAllRequiredConsentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await HasAllRequiredConsentsForTeamAsync(userId, SystemTeamIds.Volunteers, cancellationToken);
    }

    public async Task<bool> HasAllRequiredConsentsForTeamAsync(
        Guid userId,
        Guid teamId,
        CancellationToken ct = default)
    {
        var requiredVersions = await GetRequiredDocumentVersionsForTeamAsync(teamId, ct);
        if (requiredVersions.Count == 0)
        {
            return true;
        }

        var consentedVersionIds = await _consentRepository.GetConsentedVersionIdsAsync(userId, ct);
        return requiredVersions.All(v => consentedVersionIds.Contains(v.Id));
    }

    public async Task<bool> HasAnyExpiredConsentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await HasAnyExpiredConsentsForTeamAsync(userId, SystemTeamIds.Volunteers, cancellationToken);
    }

    public async Task<bool> HasAnyExpiredConsentsForTeamAsync(
        Guid userId,
        Guid teamId,
        CancellationToken ct = default)
    {
        var now = _clock.GetCurrentInstant();
        var requiredVersions = await GetRequiredDocumentVersionsForTeamAsync(teamId, ct);
        var consentedVersionIds = await _consentRepository.GetConsentedVersionIdsAsync(userId, ct);

        return requiredVersions
            .Where(v => !consentedVersionIds.Contains(v.Id))
            .Any(v =>
            {
                var gracePeriod = Duration.FromDays(v.LegalDocument.GracePeriodDays);
                return v.EffectiveFrom + gracePeriod <= now;
            });
    }

    public async Task<IReadOnlyList<Guid>> GetMissingConsentVersionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Get current versions of all required documents (Volunteers team = global)
        var requiredVersions = await GetRequiredDocumentVersionsForTeamAsync(SystemTeamIds.Volunteers, cancellationToken);
        var requiredVersionIds = requiredVersions.Select(v => v.Id).ToList();

        // Get versions the user has consented to
        var consentedVersionIds = await _consentRepository.GetConsentedVersionIdsAsync(userId, cancellationToken);

        // Find missing consents
        return requiredVersionIds
            .Where(id => !consentedVersionIds.Contains(id))
            .ToList();
    }

    public async Task<bool> HasActiveRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetCurrentInstant();

        return await _dbContext.RoleAssignments
            .AnyAsync(
                ra => ra.UserId == userId &&
                      ra.ValidFrom <= now &&
                      (ra.ValidTo == null || ra.ValidTo > now),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetUsersRequiringStatusUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        // Get all users with active roles
        var now = _clock.GetCurrentInstant();

        var usersWithActiveRoles = await _dbContext.RoleAssignments
            .AsNoTracking()
            .Where(ra => ra.ValidFrom <= now && (ra.ValidTo == null || ra.ValidTo > now))
            .Select(ra => ra.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Use batch method to avoid N+1 queries
        var usersWithAnyExpiredConsents = await GetUsersWithAnyExpiredConsentsAsync(usersWithActiveRoles, cancellationToken);

        return usersWithAnyExpiredConsents.ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetUsersWithAllRequiredConsentsAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        return await GetUsersWithAllRequiredConsentsForTeamAsync(userIds, SystemTeamIds.Volunteers, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetUsersWithAllRequiredConsentsForTeamAsync(
        IEnumerable<Guid> userIds,
        Guid teamId,
        CancellationToken ct = default)
    {
        var userIdList = userIds.ToList();
        if (userIdList.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var requiredVersions = await GetRequiredDocumentVersionsForTeamAsync(teamId, ct);
        var requiredVersionIds = requiredVersions.Select(v => v.Id).ToList();

        if (requiredVersionIds.Count == 0)
        {
            return userIdList.ToHashSet();
        }

        var consentsByUser = await _consentRepository.GetConsentedVersionIdsByUsersAsync(userIdList, ct);

        var requiredSet = requiredVersionIds.ToHashSet();
        var result = new HashSet<Guid>();

        foreach (var userId in userIdList)
        {
            if (consentsByUser.TryGetValue(userId, out var consented) &&
                requiredSet.All(consented.Contains))
            {
                result.Add(userId);
            }
        }

        return result;
    }

    public async Task<IReadOnlySet<Guid>> GetUsersWithAnyExpiredConsentsAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var userIdList = userIds.ToList();
        if (userIdList.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var now = _clock.GetCurrentInstant();

        var requiredVersions = await GetRequiredDocumentVersionsForTeamAsync(SystemTeamIds.Volunteers, cancellationToken);
        var expiredVersions = requiredVersions
            .Where(v =>
            {
                var gracePeriod = Duration.FromDays(v.LegalDocument.GracePeriodDays);
                return v.EffectiveFrom + gracePeriod <= now;
            })
            .ToList();

        if (expiredVersions.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var expiredVersionIds = expiredVersions.Select(v => v.Id).ToHashSet();

        // Get consented version IDs for all users in batch
        var consentsByUser = await _consentRepository.GetConsentedVersionIdsByUsersAsync(userIdList, cancellationToken);

        var result = new HashSet<Guid>();
        foreach (var userId in userIdList)
        {
            if (consentsByUser.TryGetValue(userId, out var consented))
            {
                // User has expired consents if any expired version is NOT in their consented list
                if (expiredVersionIds.Any(id => !consented.Contains(id)))
                {
                    result.Add(userId);
                }
            }
            else
            {
                // No consents at all, and there are expired required versions
                result.Add(userId);
            }
        }

        return result;
    }

    private async Task<List<Domain.Entities.DocumentVersion>> GetRequiredDocumentVersionsForTeamAsync(
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetCurrentInstant();

        return await _dbContext.LegalDocuments
            .AsNoTracking()
            .Where(d => d.IsRequired && d.IsActive && d.TeamId == teamId)
            .SelectMany(d => d.Versions)
            .Where(v => v.EffectiveFrom <= now)
            .Include(v => v.LegalDocument)
            .GroupBy(v => v.LegalDocumentId)
            .Select(g => g.OrderByDescending(v => v.EffectiveFrom).First())
            .ToListAsync(cancellationToken);
    }
}
