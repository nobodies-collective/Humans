using Humans.Domain.Entities;

namespace Humans.Application.Interfaces;

/// <summary>
/// CRUD for volunteer event profiles with visibility enforcement.
/// </summary>
public interface IVolunteerEventProfileService
{
    /// <summary>
    /// Gets or creates a profile for the user in the given event.
    /// </summary>
    Task<VolunteerEventProfile> GetOrCreateAsync(Guid userId, Guid eventSettingsId);

    /// <summary>
    /// Updates an event profile.
    /// </summary>
    Task UpdateAsync(VolunteerEventProfile profile);

    /// <summary>
    /// Gets a user's event profile. Medical data included only when includeMedical=true.
    /// </summary>
    Task<VolunteerEventProfile?> GetByUserAsync(Guid userId, Guid eventSettingsId, bool includeMedical);
}
