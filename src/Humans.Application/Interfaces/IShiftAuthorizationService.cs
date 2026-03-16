namespace Humans.Application.Interfaces;

/// <summary>
/// Authorization checks for shift management operations.
/// Uses cached coordinator lookups with 60-second TTL.
/// </summary>
public interface IShiftAuthorizationService
{
    /// <summary>
    /// Whether the user is a department coordinator for the given team
    /// (has a management role on a parent team).
    /// </summary>
    Task<bool> IsDeptCoordinatorAsync(Guid userId, Guid departmentTeamId);

    /// <summary>
    /// Whether the user can create/edit shifts and rotas for the department.
    /// True for dept coordinators and Admin (NOT NoInfoAdmin).
    /// </summary>
    Task<bool> CanManageShiftsAsync(Guid userId, Guid departmentTeamId);

    /// <summary>
    /// Whether the user can approve/refuse signups and voluntell for the department.
    /// True for dept coordinators, Admin, AND NoInfoAdmin.
    /// </summary>
    Task<bool> CanApproveSignupsAsync(Guid userId, Guid departmentTeamId);

    /// <summary>
    /// Gets all department team IDs where the user is a coordinator.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetCoordinatorDepartmentIdsAsync(Guid userId);
}
