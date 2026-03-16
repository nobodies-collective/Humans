using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// Calculates urgency scores for shifts to prioritize volunteer signup.
/// </summary>
public class ShiftUrgencyService : IShiftUrgencyService
{
    private static readonly Dictionary<DutyPriority, double> PriorityWeights = new()
    {
        [DutyPriority.Normal] = 1,
        [DutyPriority.Important] = 3,
        [DutyPriority.Essential] = 6
    };

    private readonly HumansDbContext _dbContext;

    public ShiftUrgencyService(HumansDbContext dbContext)
    {
        _dbContext = dbContext;
    }

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
            .Include(s => s.DutySignups)
            .Where(s => s.Rota.EventSettingsId == eventSettingsId && s.IsActive && s.Rota.IsActive);

        if (departmentId.HasValue)
            query = query.Where(s => s.Rota.TeamId == departmentId.Value);

        if (date.HasValue)
        {
            var dayOffset = Period.Between(es.GateOpeningDate, date.Value).Days;
            query = query.Where(s => s.DayOffset == dayOffset);
        }

        var shifts = await query.ToListAsync();

        var urgentShifts = shifts
            .Select(s =>
            {
                var confirmedCount = s.DutySignups.Count(d => d.Status == SignupStatus.Confirmed);
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

    public double CalculateScore(Shift shift, int confirmedCount)
    {
        var remainingSlots = Math.Max(0, shift.MaxVolunteers - confirmedCount);
        if (remainingSlots == 0) return 0;

        var priorityWeight = PriorityWeights.GetValueOrDefault(shift.Rota?.Priority ?? DutyPriority.Normal, 1);
        var durationHours = shift.Duration.TotalHours;
        var understaffedMultiplier = confirmedCount < shift.MinVolunteers ? 2 : 1;

        return remainingSlots * priorityWeight * durationHours * understaffedMultiplier;
    }
}
