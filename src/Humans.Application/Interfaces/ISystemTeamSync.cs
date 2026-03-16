namespace Humans.Application.Interfaces;

/// <summary>
/// Syncs system team memberships (Volunteers, Coordinators, Colaboradors, Asociados, Board)
/// after approval/consent/role changes.
/// </summary>
public interface ISystemTeamSync
{
    Task SyncVolunteersMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SyncCoordinatorsMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SyncColaboradorsMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SyncAsociadosMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SyncBoardTeamAsync(CancellationToken cancellationToken = default);
}
