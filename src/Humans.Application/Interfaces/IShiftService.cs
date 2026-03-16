using Humans.Domain.Entities;
using Humans.Domain.Enums;
using NodaTime;

namespace Humans.Application.Interfaces;

/// <summary>
/// CRUD for shifts with offset validation and time resolution.
/// </summary>
public interface IShiftService
{
    /// <summary>
    /// Creates a new shift. Validates DayOffset range and volunteer counts.
    /// </summary>
    Task CreateAsync(Shift shift);

    /// <summary>
    /// Updates an existing shift.
    /// </summary>
    Task UpdateAsync(Shift shift);

    /// <summary>
    /// Deactivates a shift (sets IsActive=false).
    /// </summary>
    Task DeactivateAsync(Guid shiftId);

    /// <summary>
    /// Deletes a shift. Throws if confirmed signups exist; cancels pending signups.
    /// </summary>
    Task DeleteAsync(Guid shiftId);

    /// <summary>
    /// Gets a shift by primary key.
    /// </summary>
    Task<Shift?> GetByIdAsync(Guid shiftId);

    /// <summary>
    /// Gets all shifts for a rota.
    /// </summary>
    Task<IReadOnlyList<Shift>> GetByRotaAsync(Guid rotaId);

    /// <summary>
    /// Resolves absolute times and period for a shift.
    /// </summary>
    (Instant Start, Instant End, ShiftPeriod Period) ResolveShiftTimes(Shift shift, EventSettings eventSettings);
}
