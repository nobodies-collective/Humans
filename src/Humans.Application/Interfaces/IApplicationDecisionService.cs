using NodaTime;

namespace Humans.Application.Interfaces;

/// <summary>
/// Single code path for approving or rejecting tier applications.
/// Handles state transition, term expiry, profile tier update, audit log,
/// GDPR vote cleanup, team sync, and notification email.
/// </summary>
public interface IApplicationDecisionService
{
    /// <summary>
    /// Approves a tier application.
    /// </summary>
    /// <param name="applicationId">The application to approve.</param>
    /// <param name="reviewerUserId">The user performing the approval.</param>
    /// <param name="reviewerDisplayName">Display name of the reviewer (for audit log).</param>
    /// <param name="notes">Optional decision notes.</param>
    /// <param name="boardMeetingDate">Date of the board meeting (null if admin-only decision).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure reason.</returns>
    Task<ApplicationDecisionResult> ApproveAsync(
        Guid applicationId,
        Guid reviewerUserId,
        string reviewerDisplayName,
        string? notes,
        LocalDate? boardMeetingDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a tier application.
    /// </summary>
    /// <param name="applicationId">The application to reject.</param>
    /// <param name="reviewerUserId">The user performing the rejection.</param>
    /// <param name="reviewerDisplayName">Display name of the reviewer (for audit log).</param>
    /// <param name="reason">Rejection reason (required).</param>
    /// <param name="boardMeetingDate">Date of the board meeting (null if admin-only decision).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure reason.</returns>
    Task<ApplicationDecisionResult> RejectAsync(
        Guid applicationId,
        Guid reviewerUserId,
        string reviewerDisplayName,
        string reason,
        LocalDate? boardMeetingDate,
        CancellationToken cancellationToken = default);
}

public record ApplicationDecisionResult(bool Success, string? ErrorKey = null);
