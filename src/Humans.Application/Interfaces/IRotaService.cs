using Humans.Domain.Entities;

namespace Humans.Application.Interfaces;

/// <summary>
/// CRUD for shift rotas with team and event validation.
/// </summary>
public interface IRotaService
{
    /// <summary>
    /// Creates a new rota. Validates team is a department and event is active.
    /// </summary>
    Task CreateAsync(Rota rota);

    /// <summary>
    /// Updates an existing rota.
    /// </summary>
    Task UpdateAsync(Rota rota);

    /// <summary>
    /// Deactivates a rota (sets IsActive=false).
    /// </summary>
    Task DeactivateAsync(Guid rotaId);

    /// <summary>
    /// Deletes a rota. Throws if child shifts have confirmed signups.
    /// </summary>
    Task DeleteAsync(Guid rotaId);

    /// <summary>
    /// Gets a rota by primary key with shifts included.
    /// </summary>
    Task<Rota?> GetByIdAsync(Guid rotaId);

    /// <summary>
    /// Gets all rotas for a department in an event.
    /// </summary>
    Task<IReadOnlyList<Rota>> GetByDepartmentAsync(Guid teamId, Guid eventSettingsId);
}
