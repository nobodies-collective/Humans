using Humans.Domain.Entities;

namespace Humans.Application.Interfaces;

/// <summary>
/// CRUD and active event resolution for EventSettings.
/// </summary>
public interface IEventSettingsService
{
    /// <summary>
    /// Gets the single active EventSettings, or null if none.
    /// </summary>
    Task<EventSettings?> GetActiveAsync();

    /// <summary>
    /// Gets an EventSettings by primary key.
    /// </summary>
    Task<EventSettings?> GetByIdAsync(Guid id);

    /// <summary>
    /// Creates a new EventSettings. Validates only one IsActive=true.
    /// </summary>
    Task CreateAsync(EventSettings entity);

    /// <summary>
    /// Updates an existing EventSettings.
    /// </summary>
    Task UpdateAsync(EventSettings entity);

    /// <summary>
    /// Gets the available (non-barrios) EE slots for a given day offset.
    /// </summary>
    int GetAvailableEeSlots(EventSettings settings, int dayOffset);
}
