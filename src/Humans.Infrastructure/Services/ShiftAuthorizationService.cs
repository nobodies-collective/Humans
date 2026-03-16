using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// Checks shift management authorization using cached department coordinator lookups.
/// </summary>
public class ShiftAuthorizationService : IShiftAuthorizationService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly HumansDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;

    public ShiftAuthorizationService(HumansDbContext dbContext, IMemoryCache cache, IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _clock = clock;
    }

    public async Task<bool> IsDeptCoordinatorAsync(Guid userId, Guid departmentTeamId)
    {
        var deptIds = await GetCoordinatorDepartmentIdsAsync(userId);
        return deptIds.Contains(departmentTeamId);
    }

    public async Task<bool> CanManageShiftsAsync(Guid userId, Guid departmentTeamId)
    {
        // Admin can manage all shifts; NoInfoAdmin CANNOT
        if (await HasActiveRoleAsync(userId, RoleNames.Admin))
            return true;

        return await IsDeptCoordinatorAsync(userId, departmentTeamId);
    }

    public async Task<bool> CanApproveSignupsAsync(Guid userId, Guid departmentTeamId)
    {
        // Admin and NoInfoAdmin can approve signups
        if (await HasActiveRoleAsync(userId, RoleNames.Admin) ||
            await HasActiveRoleAsync(userId, RoleNames.NoInfoAdmin))
            return true;

        return await IsDeptCoordinatorAsync(userId, departmentTeamId);
    }

    public async Task<IReadOnlyList<Guid>> GetCoordinatorDepartmentIdsAsync(Guid userId)
    {
        var cacheKey = $"shift-auth:{userId}";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await LoadCoordinatorDepartmentIdsAsync(userId);
        });
        return result!;
    }

    private async Task<IReadOnlyList<Guid>> LoadCoordinatorDepartmentIdsAsync(Guid userId)
    {
        // Find parent teams (departments) where user has a management role.
        // Path: TeamMembers → TeamRoleAssignments → TeamRoleDefinitions (IsManagement=true)
        // The team must be a parent team (ParentTeamId IS NULL) and not a system team.
        return await _dbContext.TeamRoleAssignments
            .AsNoTracking()
            .Where(tra =>
                tra.TeamMember.UserId == userId &&
                tra.TeamMember.LeftAt == null &&
                tra.TeamRoleDefinition.IsManagement &&
                tra.TeamRoleDefinition.Team.ParentTeamId == null &&
                tra.TeamRoleDefinition.Team.SystemTeamType == SystemTeamType.None)
            .Select(tra => tra.TeamRoleDefinition.TeamId)
            .Distinct()
            .ToListAsync();
    }

    private async Task<bool> HasActiveRoleAsync(Guid userId, string roleName)
    {
        var now = _clock.GetCurrentInstant();
        return await _dbContext.RoleAssignments
            .AsNoTracking()
            .AnyAsync(ra =>
                ra.UserId == userId &&
                string.Equals(ra.RoleName, roleName) &&
                ra.ValidFrom <= now &&
                (ra.ValidTo == null || ra.ValidTo > now));
    }
}
