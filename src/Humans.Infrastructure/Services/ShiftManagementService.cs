using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// Consolidated service for shift management: authorization, event settings,
/// rotas, shifts, and urgency scoring.
/// </summary>
public class ShiftManagementService : IShiftManagementService
{
    private static readonly TimeSpan AuthCacheDuration = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<ShiftPriority, double> PriorityWeights = new()
    {
        [ShiftPriority.Normal] = 1,
        [ShiftPriority.Important] = 3,
        [ShiftPriority.Essential] = 6
    };

    private readonly HumansDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly ILogger<ShiftManagementService> _logger;

    public ShiftManagementService(
        HumansDbContext dbContext,
        IMemoryCache cache,
        IClock clock,
        ILogger<ShiftManagementService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    // ============================================================
    // Authorization
    // ============================================================

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
            entry.AbsoluteExpirationRelativeToNow = AuthCacheDuration;
            return await LoadCoordinatorDepartmentIdsAsync(userId);
        });
        return result!;
    }

    private async Task<IReadOnlyList<Guid>> LoadCoordinatorDepartmentIdsAsync(Guid userId)
    {
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
                ra.RoleName == roleName &&
                ra.ValidFrom <= now &&
                (ra.ValidTo == null || ra.ValidTo > now));
    }

    // ============================================================
    // EventSettings
    // ============================================================

    public async Task<EventSettings?> GetActiveAsync()
    {
        return await _dbContext.EventSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IsActive);
    }

    public async Task<EventSettings?> GetByIdAsync(Guid id)
    {
        return await _dbContext.EventSettings.FindAsync(id);
    }

    public async Task CreateAsync(EventSettings entity)
    {
        if (entity.IsActive)
        {
            var existing = await _dbContext.EventSettings
                .AnyAsync(e => e.IsActive);
            if (existing)
                throw new InvalidOperationException("Only one EventSettings can be active at a time.");
        }

        entity.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.EventSettings.Add(entity);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(EventSettings entity)
    {
        if (entity.IsActive)
        {
            var existing = await _dbContext.EventSettings
                .AnyAsync(e => e.IsActive && e.Id != entity.Id);
            if (existing)
                throw new InvalidOperationException("Only one EventSettings can be active at a time.");
        }

        entity.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.EventSettings.Update(entity);
        await _dbContext.SaveChangesAsync();
    }

    public int GetAvailableEeSlots(EventSettings settings, int dayOffset)
    {
        var totalCapacity = settings.GetEarlyEntryCapacityForDay(dayOffset);
        if (totalCapacity == 0) return 0;

        var barriosAllocation = 0;
        if (settings.BarriosEarlyEntryAllocation != null)
        {
            var applicableKey = int.MinValue;
            foreach (var key in settings.BarriosEarlyEntryAllocation.Keys)
            {
                if (key <= dayOffset && key > applicableKey)
                    applicableKey = key;
            }
            if (applicableKey != int.MinValue)
                barriosAllocation = settings.BarriosEarlyEntryAllocation[applicableKey];
        }

        return Math.Max(0, totalCapacity - barriosAllocation);
    }

    // ============================================================
    // Rota
    // ============================================================

    public async Task CreateRotaAsync(Rota rota)
    {
        var team = await _dbContext.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == rota.TeamId);

        if (team == null)
            throw new InvalidOperationException("Team not found.");
        if (team.ParentTeamId != null)
            throw new InvalidOperationException("Rotas can only be created on parent teams (departments).");
        if (team.SystemTeamType != SystemTeamType.None)
            throw new InvalidOperationException("Rotas cannot be created on system teams.");

        var eventSettings = await _dbContext.EventSettings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == rota.EventSettingsId && e.IsActive);
        if (eventSettings == null)
            throw new InvalidOperationException("Active EventSettings not found.");

        rota.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Rotas.Add(rota);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateRotaAsync(Rota rota)
    {
        rota.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Rotas.Update(rota);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeactivateRotaAsync(Guid rotaId)
    {
        var rota = await _dbContext.Rotas.FindAsync(rotaId);
        if (rota == null) throw new InvalidOperationException("Rota not found.");
        rota.IsActive = false;
        rota.UpdatedAt = _clock.GetCurrentInstant();
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteRotaAsync(Guid rotaId)
    {
        var rota = await _dbContext.Rotas
            .Include(r => r.Shifts)
                .ThenInclude(s => s.ShiftSignups)
            .FirstOrDefaultAsync(r => r.Id == rotaId);

        if (rota == null) throw new InvalidOperationException("Rota not found.");

        var hasConfirmedSignups = rota.Shifts
            .SelectMany(s => s.ShiftSignups)
            .Any(d => d.Status == SignupStatus.Confirmed);

        if (hasConfirmedSignups)
            throw new InvalidOperationException("Cannot delete rota with confirmed signups.");

        // Cancel pending signups and remove all signups before deleting
        // (ShiftSignup→Shift FK is Restrict, so cascade won't handle them)
        foreach (var shift in rota.Shifts)
        {
            foreach (var signup in shift.ShiftSignups.Where(d => d.Status == SignupStatus.Pending).ToList())
            {
                signup.Cancel(_clock, "Rota deleted");
            }
            _dbContext.ShiftSignups.RemoveRange(shift.ShiftSignups);
        }

        _dbContext.Rotas.Remove(rota);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Rota?> GetRotaByIdAsync(Guid rotaId)
    {
        return await _dbContext.Rotas
            .Include(r => r.Shifts)
            .Include(r => r.Team)
            .FirstOrDefaultAsync(r => r.Id == rotaId);
    }

    public async Task<IReadOnlyList<Rota>> GetRotasByDepartmentAsync(Guid teamId, Guid eventSettingsId)
    {
        return await _dbContext.Rotas
            .Include(r => r.EventSettings)
            .Include(r => r.Shifts)
                .ThenInclude(s => s.ShiftSignups)
                    .ThenInclude(su => su.User)
            .Where(r => r.TeamId == teamId && r.EventSettingsId == eventSettingsId)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    // ============================================================
    // Shift
    // ============================================================

    public async Task CreateShiftAsync(Shift shift)
    {
        var rota = await _dbContext.Rotas
            .Include(r => r.EventSettings)
            .FirstOrDefaultAsync(r => r.Id == shift.RotaId);

        if (rota == null) throw new InvalidOperationException("Rota not found.");

        var es = rota.EventSettings;
        if (shift.DayOffset < es.BuildStartOffset || shift.DayOffset > es.StrikeEndOffset)
            throw new InvalidOperationException(
                $"DayOffset {shift.DayOffset} is outside the valid range ({es.BuildStartOffset}..{es.StrikeEndOffset}).");

        if (shift.MinVolunteers > shift.MaxVolunteers)
            throw new InvalidOperationException("MinVolunteers cannot exceed MaxVolunteers.");

        shift.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Shifts.Add(shift);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateShiftAsync(Shift shift)
    {
        shift.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Shifts.Update(shift);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeactivateShiftAsync(Guid shiftId)
    {
        var shift = await _dbContext.Shifts.FindAsync(shiftId);
        if (shift == null) throw new InvalidOperationException("Shift not found.");
        shift.IsActive = false;
        shift.UpdatedAt = _clock.GetCurrentInstant();
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteShiftAsync(Guid shiftId)
    {
        var shift = await _dbContext.Shifts
            .Include(s => s.ShiftSignups)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift == null) throw new InvalidOperationException("Shift not found.");

        var hasConfirmed = shift.ShiftSignups.Any(d => d.Status == SignupStatus.Confirmed);
        if (hasConfirmed)
            throw new InvalidOperationException("Cannot delete shift with confirmed signups.");

        // Cancel pending signups, then remove all signups (confirmed already blocked above)
        foreach (var signup in shift.ShiftSignups.Where(d => d.Status == SignupStatus.Pending))
        {
            signup.Cancel(_clock, "Shift deleted");
        }

        _dbContext.ShiftSignups.RemoveRange(shift.ShiftSignups);
        _dbContext.Shifts.Remove(shift);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Shift?> GetShiftByIdAsync(Guid shiftId)
    {
        return await _dbContext.Shifts
            .Include(s => s.Rota)
                .ThenInclude(r => r.Team)
            .Include(s => s.ShiftSignups)
            .FirstOrDefaultAsync(s => s.Id == shiftId);
    }

    public async Task<IReadOnlyList<Shift>> GetShiftsByRotaAsync(Guid rotaId)
    {
        return await _dbContext.Shifts
            .Include(s => s.ShiftSignups)
            .Where(s => s.RotaId == rotaId)
            .OrderBy(s => s.DayOffset)
            .ThenBy(s => s.StartTime)
            .ToListAsync();
    }

    public (Instant Start, Instant End, ShiftPeriod Period) ResolveShiftTimes(Shift shift, EventSettings eventSettings)
    {
        var start = shift.GetAbsoluteStart(eventSettings);
        var end = shift.GetAbsoluteEnd(eventSettings);
        var period = shift.GetShiftPeriod(eventSettings);
        return (start, end, period);
    }

    // ============================================================
    // Urgency
    // ============================================================

    public async Task<IReadOnlyList<UrgentShift>> GetUrgentShiftsAsync(
        Guid eventSettingsId, int? limit = null,
        Guid? departmentId = null, LocalDate? date = null)
    {
        var es = await _dbContext.EventSettings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventSettingsId);
        if (es == null) return [];

        var query = _dbContext.Shifts
            .AsNoTracking()
            .Include(s => s.Rota).ThenInclude(r => r.Team)
            .Include(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Include(s => s.ShiftSignups)
            .Where(s => s.Rota.EventSettingsId == eventSettingsId && s.IsActive && s.Rota.IsActive);

        if (departmentId.HasValue)
            query = query.Where(s => s.Rota.TeamId == departmentId.Value);

        if (date.HasValue)
        {
            var dayOffset = Period.Between(es.GateOpeningDate, date.Value).Days;
            query = query.Where(s => s.DayOffset == dayOffset);
        }

        var shifts = await query.ToListAsync();

        var now = _clock.GetCurrentInstant();
        var urgentShifts = shifts
            .Where(s => s.GetAbsoluteEnd(es) > now)
            .Select(s =>
            {
                var confirmedCount = s.ShiftSignups.Count(d => d.Status == SignupStatus.Confirmed);
                var score = CalculateScore(s, confirmedCount);
                var remaining = Math.Max(0, s.MaxVolunteers - confirmedCount);
                return new UrgentShift(s, score, confirmedCount, remaining, s.Rota.Team.Name);
            })
            .Where(u => u.UrgencyScore > 0)
            .OrderByDescending(u => u.UrgencyScore)
            .ToList();

        if (limit.HasValue)
            return urgentShifts.Take(limit.Value).ToList();

        return urgentShifts;
    }

    public async Task<IReadOnlyList<UrgentShift>> GetBrowseShiftsAsync(
        Guid eventSettingsId, Guid? departmentId = null, LocalDate? date = null,
        bool includeAdminOnly = false)
    {
        var es = await _dbContext.EventSettings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventSettingsId);
        if (es == null) return [];

        var query = _dbContext.Shifts
            .Include(s => s.Rota).ThenInclude(r => r.Team)
            .Include(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Include(s => s.ShiftSignups)
            .Where(s => s.Rota.EventSettingsId == eventSettingsId && s.IsActive && s.Rota.IsActive);

        if (!includeAdminOnly)
            query = query.Where(s => !s.AdminOnly);

        if (departmentId.HasValue)
            query = query.Where(s => s.Rota.TeamId == departmentId.Value);

        if (date.HasValue)
        {
            var dayOffset = Period.Between(es.GateOpeningDate, date.Value).Days;
            query = query.Where(s => s.DayOffset == dayOffset);
        }

        var shifts = await query.ToListAsync();

        return shifts
            .Select(s =>
            {
                var confirmedCount = s.ShiftSignups.Count(d => d.Status == SignupStatus.Confirmed);
                var score = CalculateScore(s, confirmedCount);
                var remaining = Math.Max(0, s.MaxVolunteers - confirmedCount);
                return new UrgentShift(s, score, confirmedCount, remaining, s.Rota.Team.Name);
            })
            .OrderByDescending(u => u.UrgencyScore)
            .ToList();
    }

    public double CalculateScore(Shift shift, int confirmedCount)
    {
        var remainingSlots = Math.Max(0, shift.MaxVolunteers - confirmedCount);
        if (remainingSlots == 0) return 0;

        var priorityWeight = PriorityWeights.GetValueOrDefault(shift.Rota?.Priority ?? ShiftPriority.Normal, 1);
        var durationHours = shift.Duration.TotalHours;
        var understaffedMultiplier = confirmedCount < shift.MinVolunteers ? 2 : 1;

        return remainingSlots * priorityWeight * durationHours * understaffedMultiplier;
    }

    // ============================================================
    // Staffing & Summary
    // ============================================================

    public async Task<IReadOnlyList<DailyStaffingData>> GetStaffingDataAsync(
        Guid eventSettingsId, Guid? departmentId = null)
    {
        var es = await _dbContext.EventSettings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventSettingsId);
        if (es == null) return [];

        var tz = DateTimeZoneProviders.Tzdb[es.TimeZoneId];

        // Build period: [BuildStartOffset..-1] and Strike period: [EventEndOffset+1..StrikeEndOffset]
        var dayOffsets = new List<int>();
        for (var d = es.BuildStartOffset; d < 0; d++) dayOffsets.Add(d);
        for (var d = es.EventEndOffset + 1; d <= es.StrikeEndOffset; d++) dayOffsets.Add(d);

        if (dayOffsets.Count == 0) return [];

        var query = _dbContext.Shifts
            .AsNoTracking()
            .Include(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Include(s => s.ShiftSignups)
            .Where(s => s.Rota.EventSettingsId == eventSettingsId && s.IsActive && s.Rota.IsActive);

        if (departmentId.HasValue)
            query = query.Where(s => s.Rota.TeamId == departmentId.Value);

        var shifts = await query.ToListAsync();
        var results = new List<DailyStaffingData>();

        foreach (var dayOffset in dayOffsets)
        {
            var dayDate = es.GateOpeningDate.PlusDays(dayOffset);
            var dayStart = dayDate.AtStartOfDayInZone(tz).ToInstant();
            var dayEnd = dayDate.PlusDays(1).AtStartOfDayInZone(tz).ToInstant();
            var period = dayOffset < 0 ? "Build" : "Strike";
            var dateLabel = dayDate.DayOfWeek.ToString()[..3] + " " + dayDate.ToString("MMM d", null);

            var overlapping = shifts.Where(s =>
            {
                var start = s.GetAbsoluteStart(es);
                var end = s.GetAbsoluteEnd(es);
                return start < dayEnd && end > dayStart;
            }).ToList();

            var totalSlots = overlapping.Sum(s => s.MaxVolunteers);
            var confirmedCount = overlapping
                .SelectMany(s => s.ShiftSignups)
                .Count(su => su.Status == SignupStatus.Confirmed);

            results.Add(new DailyStaffingData(dayOffset, dateLabel, confirmedCount, totalSlots, period));
        }

        return results;
    }

    public async Task<ShiftsSummaryData?> GetShiftsSummaryAsync(
        Guid eventSettingsId, Guid departmentTeamId)
    {
        var rotas = await _dbContext.Rotas
            .AsNoTracking()
            .Include(r => r.Shifts).ThenInclude(s => s.ShiftSignups)
            .Where(r => r.EventSettingsId == eventSettingsId && r.TeamId == departmentTeamId)
            .ToListAsync();

        if (rotas.Count == 0) return null;

        var activeShifts = rotas.SelectMany(r => r.Shifts).Where(s => s.IsActive).ToList();
        if (activeShifts.Count == 0) return null;

        var allSignups = activeShifts.SelectMany(s => s.ShiftSignups).ToList();

        return new ShiftsSummaryData(
            TotalSlots: activeShifts.Sum(s => s.MaxVolunteers),
            ConfirmedCount: allSignups.Count(s => s.Status == SignupStatus.Confirmed),
            PendingCount: allSignups.Count(s => s.Status == SignupStatus.Pending),
            UniqueVolunteerCount: allSignups
                .Where(s => s.Status == SignupStatus.Confirmed)
                .Select(s => s.UserId)
                .Distinct()
                .Count());
    }

    public async Task<IReadOnlyList<(Guid TeamId, string TeamName)>> GetDepartmentsWithRotasAsync(
        Guid eventSettingsId)
    {
        var teams = await _dbContext.Rotas
            .AsNoTracking()
            .Where(r => r.EventSettingsId == eventSettingsId && r.IsActive)
            .Select(r => new { r.Team.Id, r.Team.Name })
            .Distinct()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return teams.Select(x => (x.Id, x.Name)).ToList();
    }
}
