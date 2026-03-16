using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// CRUD for shifts with offset validation and time resolution.
/// </summary>
public class ShiftService : IShiftService
{
    private readonly HumansDbContext _dbContext;
    private readonly IClock _clock;

    public ShiftService(HumansDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task CreateAsync(Shift shift)
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

    public async Task UpdateAsync(Shift shift)
    {
        shift.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Shifts.Update(shift);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid shiftId)
    {
        var shift = await _dbContext.Shifts.FindAsync(shiftId);
        if (shift == null) throw new InvalidOperationException("Shift not found.");
        shift.IsActive = false;
        shift.UpdatedAt = _clock.GetCurrentInstant();
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid shiftId)
    {
        var shift = await _dbContext.Shifts
            .Include(s => s.DutySignups)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift == null) throw new InvalidOperationException("Shift not found.");

        var hasConfirmed = shift.DutySignups.Any(d => d.Status == SignupStatus.Confirmed);
        if (hasConfirmed)
            throw new InvalidOperationException("Cannot delete shift with confirmed signups.");

        // Cancel pending signups
        foreach (var signup in shift.DutySignups.Where(d => d.Status == SignupStatus.Pending))
        {
            signup.Cancel(_clock, "Shift deleted");
        }

        _dbContext.Shifts.Remove(shift);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Shift?> GetByIdAsync(Guid shiftId)
    {
        return await _dbContext.Shifts
            .Include(s => s.Rota)
                .ThenInclude(r => r.Team)
            .Include(s => s.DutySignups)
            .FirstOrDefaultAsync(s => s.Id == shiftId);
    }

    public async Task<IReadOnlyList<Shift>> GetByRotaAsync(Guid rotaId)
    {
        return await _dbContext.Shifts
            .Include(s => s.DutySignups)
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
}
