using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// CRUD for volunteer event profiles with visibility enforcement.
/// </summary>
public class VolunteerEventProfileService : IVolunteerEventProfileService
{
    private readonly HumansDbContext _dbContext;
    private readonly IClock _clock;

    public VolunteerEventProfileService(HumansDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<VolunteerEventProfile> GetOrCreateAsync(Guid userId, Guid eventSettingsId)
    {
        var existing = await _dbContext.VolunteerEventProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.EventSettingsId == eventSettingsId);

        if (existing != null)
            return existing;

        var now = _clock.GetCurrentInstant();
        var profile = new VolunteerEventProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventSettingsId = eventSettingsId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.VolunteerEventProfiles.Add(profile);
        await _dbContext.SaveChangesAsync();
        return profile;
    }

    public async Task UpdateAsync(VolunteerEventProfile profile)
    {
        profile.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.VolunteerEventProfiles.Update(profile);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<VolunteerEventProfile?> GetByUserAsync(Guid userId, Guid eventSettingsId, bool includeMedical)
    {
        var profile = await _dbContext.VolunteerEventProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.EventSettingsId == eventSettingsId);

        if (profile != null && !includeMedical)
        {
            profile.MedicalConditions = null;
        }

        return profile;
    }
}
