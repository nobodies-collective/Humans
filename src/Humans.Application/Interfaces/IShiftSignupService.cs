using Humans.Domain.Entities;

namespace Humans.Application.Interfaces;

/// <summary>
/// Manages the shift signup state machine with invariant enforcement.
/// </summary>
public interface IShiftSignupService
{
    /// <summary>
    /// Creates a signup for a user on a shift. Auto-confirms for Public policy.
    /// </summary>
    Task<SignupResult> SignUpAsync(Guid userId, Guid shiftId, Guid? actorUserId = null);

    /// <summary>
    /// Approves a pending signup. Re-validates invariants.
    /// </summary>
    Task<SignupResult> ApproveAsync(Guid signupId, Guid reviewerUserId);

    /// <summary>
    /// Refuses a pending signup.
    /// </summary>
    Task<SignupResult> RefuseAsync(Guid signupId, Guid reviewerUserId, string? reason);

    /// <summary>
    /// Bails from a confirmed or pending signup.
    /// </summary>
    Task<SignupResult> BailAsync(Guid signupId, Guid actorUserId, string? reason);

    /// <summary>
    /// Creates a confirmed signup on behalf of a volunteer (voluntell).
    /// </summary>
    Task<SignupResult> VoluntellAsync(Guid userId, Guid shiftId, Guid enrollerUserId);

    /// <summary>
    /// Marks a confirmed signup as no-show (post-shift only).
    /// </summary>
    Task<SignupResult> MarkNoShowAsync(Guid signupId, Guid reviewerUserId);

    /// <summary>
    /// Gets all signups for a user, optionally filtered by event.
    /// </summary>
    Task<IReadOnlyList<ShiftSignup>> GetByUserAsync(Guid userId, Guid? eventSettingsId = null);

    /// <summary>
    /// Gets a signup by primary key with Shift.Rota.Team included.
    /// </summary>
    Task<ShiftSignup?> GetByIdAsync(Guid signupId);

    /// <summary>
    /// Gets all signups for a shift.
    /// </summary>
    Task<IReadOnlyList<ShiftSignup>> GetByShiftAsync(Guid shiftId);

    /// <summary>
    /// Gets all no-show signups for a user, with shift/team context and reviewer info.
    /// </summary>
    Task<IReadOnlyList<ShiftSignup>> GetNoShowHistoryAsync(Guid userId);
}

/// <summary>
/// Result of a signup operation.
/// </summary>
public record SignupResult
{
    public bool Success { get; init; }
    public string? Warning { get; init; }
    public string? Error { get; init; }
    public ShiftSignup? Signup { get; init; }

    public static SignupResult Ok(ShiftSignup signup, string? warning = null) =>
        new() { Success = true, Signup = signup, Warning = warning };

    public static SignupResult Fail(string error) =>
        new() { Success = false, Error = error };
}
