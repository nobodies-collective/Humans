using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// CRUD and active event resolution for EventSettings.
/// </summary>
public class EventSettingsService : IEventSettingsService
{
    private readonly HumansDbContext _dbContext;
    private readonly IClock _clock;

    public EventSettingsService(HumansDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

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
}
