using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// Manages the duty signup state machine with invariant enforcement.
/// </summary>
public class DutySignupService : IDutySignupService
{
    private readonly HumansDbContext _dbContext;
    private readonly IEventSettingsService _eventSettingsService;
    private readonly IShiftAuthorizationService _authService;
    private readonly IAuditLogService _auditLogService;
    private readonly IClock _clock;
    private readonly ILogger<DutySignupService> _logger;

    public DutySignupService(
        HumansDbContext dbContext,
        IEventSettingsService eventSettingsService,
        IShiftAuthorizationService authService,
        IAuditLogService auditLogService,
        IClock clock,
        ILogger<DutySignupService> logger)
    {
        _dbContext = dbContext;
        _eventSettingsService = eventSettingsService;
        _authService = authService;
        _auditLogService = auditLogService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SignupResult> SignUpAsync(Guid userId, Guid shiftId, Guid? actorUserId = null)
    {
        var shift = await _dbContext.Shifts
            .Include(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Include(s => s.Rota).ThenInclude(r => r.Team)
            .Include(s => s.DutySignups)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift == null) return SignupResult.Fail("Shift not found.");
        if (!shift.IsActive) return SignupResult.Fail("Shift is not active.");
        if (!shift.Rota.IsActive) return SignupResult.Fail("Rota is not active.");

        var es = shift.Rota.EventSettings;
        var now = _clock.GetCurrentInstant();
        var isPrivileged = await IsPrivilegedAsync(userId, shift.Rota.TeamId);

        // System open check
        if (!es.IsShiftBrowsingOpen && !isPrivileged)
            return SignupResult.Fail("Shift browsing is not currently open.");

        // AdminOnly check
        if (shift.AdminOnly && !isPrivileged)
            return SignupResult.Fail("This shift is restricted to coordinators and admins.");

        // EE freeze check for build shifts
        if (shift.IsEarlyEntry && es.EarlyEntryClose.HasValue && now >= es.EarlyEntryClose.Value && !isPrivileged)
            return SignupResult.Fail("Early entry signups are closed.");

        // Overlap check
        var overlapWarning = await CheckOverlapAsync(userId, shift, es);
        if (overlapWarning != null)
            return SignupResult.Fail(overlapWarning);

        // Capacity warning
        string? warning = null;
        var confirmedCount = shift.DutySignups.Count(d => d.Status == SignupStatus.Confirmed);
        if (confirmedCount >= shift.MaxVolunteers)
            warning = "This shift is at capacity.";

        // EE cap warning
        if (shift.IsEarlyEntry)
        {
            var eeWarning = await CheckEeCapAsync(es, shift.DayOffset);
            if (eeWarning != null)
                warning = warning == null ? eeWarning : $"{warning} {eeWarning}";
        }

        // Determine initial status
        var isDeptCoord = await _authService.IsDeptCoordinatorAsync(userId, shift.Rota.TeamId);
        var autoConfirm = shift.Rota.Policy == SignupPolicy.Public || isDeptCoord;

        var signup = new DutySignup
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ShiftId = shiftId,
            Status = autoConfirm ? SignupStatus.Confirmed : SignupStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (autoConfirm)
        {
            signup.ReviewedByUserId = actorUserId ?? userId;
            signup.ReviewedAt = now;
        }

        _dbContext.DutySignups.Add(signup);

        if (autoConfirm)
        {
            await _auditLogService.LogAsync(
                AuditAction.DutySignupConfirmed, nameof(DutySignup), signup.Id,
                $"Auto-confirmed signup for shift '{shift.Title}'",
                userId, "Self");
        }

        await _dbContext.SaveChangesAsync();

        return SignupResult.Ok(signup, warning);
    }

    public async Task<SignupResult> ApproveAsync(Guid signupId, Guid reviewerUserId)
    {
        var signup = await LoadSignupWithShiftAsync(signupId);
        if (signup == null) return SignupResult.Fail("Signup not found.");

        if (signup.Status != SignupStatus.Pending)
            return SignupResult.Fail($"Cannot approve signup in {signup.Status} state.");

        var es = signup.Shift.Rota.EventSettings;

        // Re-validate invariants
        var overlapWarning = await CheckOverlapAsync(signup.UserId, signup.Shift, es);
        string? warning = null;
        if (overlapWarning != null)
            warning = $"Warning: {overlapWarning}";

        var confirmedCount = signup.Shift.DutySignups.Count(d => d.Status == SignupStatus.Confirmed);
        if (confirmedCount >= signup.Shift.MaxVolunteers)
            warning = warning == null ? "Warning: shift is at capacity." : $"{warning} Shift is at capacity.";

        signup.Confirm(reviewerUserId, _clock);

        await _auditLogService.LogAsync(
            AuditAction.DutySignupConfirmed, nameof(DutySignup), signup.Id,
            $"Approved signup for shift '{signup.Shift.Title}'",
            reviewerUserId, "Reviewer");

        await _dbContext.SaveChangesAsync();

        return SignupResult.Ok(signup, warning);
    }

    public async Task<SignupResult> RefuseAsync(Guid signupId, Guid reviewerUserId, string? reason)
    {
        var signup = await LoadSignupWithShiftAsync(signupId);
        if (signup == null) return SignupResult.Fail("Signup not found.");

        signup.Refuse(reviewerUserId, _clock, reason);

        await _auditLogService.LogAsync(
            AuditAction.DutySignupRefused, nameof(DutySignup), signup.Id,
            $"Refused signup for shift '{signup.Shift.Title}'" + (reason != null ? $": {reason}" : ""),
            reviewerUserId, "Reviewer");

        await _dbContext.SaveChangesAsync();

        return SignupResult.Ok(signup);
    }

    public async Task<SignupResult> BailAsync(Guid signupId, Guid actorUserId, string? reason)
    {
        var signup = await LoadSignupWithShiftAsync(signupId);
        if (signup == null) return SignupResult.Fail("Signup not found.");

        var es = signup.Shift.Rota.EventSettings;
        var now = _clock.GetCurrentInstant();
        var isPrivileged = await IsPrivilegedAsync(actorUserId, signup.Shift.Rota.TeamId);

        // EE freeze check for build shifts
        if (signup.Shift.IsEarlyEntry && es.EarlyEntryClose.HasValue && now >= es.EarlyEntryClose.Value && !isPrivileged)
            return SignupResult.Fail("Cannot bail from build shifts after early entry close.");

        signup.Bail(actorUserId, _clock, reason);

        await _auditLogService.LogAsync(
            AuditAction.DutySignupBailed, nameof(DutySignup), signup.Id,
            $"Bailed from shift '{signup.Shift.Title}'" + (reason != null ? $": {reason}" : ""),
            actorUserId, "Actor");

        await _dbContext.SaveChangesAsync();

        return SignupResult.Ok(signup);
    }

    public async Task<SignupResult> VoluntellAsync(Guid userId, Guid shiftId, Guid enrollerUserId)
    {
        var shift = await _dbContext.Shifts
            .Include(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Include(s => s.DutySignups)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift == null) return SignupResult.Fail("Shift not found.");

        var es = shift.Rota.EventSettings;
        var now = _clock.GetCurrentInstant();

        // Overlap check
        var overlapWarning = await CheckOverlapAsync(userId, shift, es);
        if (overlapWarning != null)
            return SignupResult.Fail(overlapWarning);

        var signup = new DutySignup
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ShiftId = shiftId,
            Status = SignupStatus.Confirmed,
            Enrolled = true,
            EnrolledByUserId = enrollerUserId,
            ReviewedByUserId = enrollerUserId,
            ReviewedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.DutySignups.Add(signup);

        await _auditLogService.LogAsync(
            AuditAction.DutySignupVoluntold, nameof(DutySignup), signup.Id,
            $"Voluntold for shift '{shift.Title}'",
            enrollerUserId, "Enroller",
            userId, nameof(User));

        await _dbContext.SaveChangesAsync();

        return SignupResult.Ok(signup);
    }

    public async Task<SignupResult> MarkNoShowAsync(Guid signupId, Guid reviewerUserId)
    {
        var signup = await LoadSignupWithShiftAsync(signupId);
        if (signup == null) return SignupResult.Fail("Signup not found.");

        var es = signup.Shift.Rota.EventSettings;
        var shiftEnd = signup.Shift.GetAbsoluteEnd(es);
        var now = _clock.GetCurrentInstant();

        if (now < shiftEnd)
            return SignupResult.Fail("Cannot mark no-show before the shift ends.");

        signup.MarkNoShow(reviewerUserId, _clock);

        await _auditLogService.LogAsync(
            AuditAction.DutySignupNoShow, nameof(DutySignup), signup.Id,
            $"Marked no-show for shift '{signup.Shift.Title}'",
            reviewerUserId, "Reviewer");

        await _dbContext.SaveChangesAsync();

        return SignupResult.Ok(signup);
    }

    public async Task<IReadOnlyList<DutySignup>> GetByUserAsync(Guid userId, Guid? eventSettingsId = null)
    {
        var query = _dbContext.DutySignups
            .Include(d => d.Shift).ThenInclude(s => s.Rota).ThenInclude(r => r.Team)
            .Include(d => d.Shift).ThenInclude(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Where(d => d.UserId == userId);

        if (eventSettingsId.HasValue)
            query = query.Where(d => d.Shift.Rota.EventSettingsId == eventSettingsId.Value);

        return await query.OrderBy(d => d.Shift.DayOffset).ThenBy(d => d.Shift.StartTime).ToListAsync();
    }

    public async Task<IReadOnlyList<DutySignup>> GetByShiftAsync(Guid shiftId)
    {
        return await _dbContext.DutySignups
            .Include(d => d.User)
            .Where(d => d.ShiftId == shiftId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    private async Task<DutySignup?> LoadSignupWithShiftAsync(Guid signupId)
    {
        return await _dbContext.DutySignups
            .Include(d => d.Shift).ThenInclude(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Include(d => d.Shift).ThenInclude(s => s.Rota).ThenInclude(r => r.Team)
            .Include(d => d.Shift).ThenInclude(s => s.DutySignups)
            .FirstOrDefaultAsync(d => d.Id == signupId);
    }

    private async Task<string?> CheckOverlapAsync(Guid userId, Shift targetShift, EventSettings es)
    {
        var targetStart = targetShift.GetAbsoluteStart(es);
        var targetEnd = targetShift.GetAbsoluteEnd(es);

        var userSignups = await _dbContext.DutySignups
            .Include(d => d.Shift).ThenInclude(s => s.Rota).ThenInclude(r => r.EventSettings)
            .Where(d => d.UserId == userId &&
                        d.ShiftId != targetShift.Id &&
                        (d.Status == SignupStatus.Confirmed || d.Status == SignupStatus.Pending))
            .ToListAsync();

        foreach (var existing in userSignups)
        {
            var existingEs = existing.Shift.Rota.EventSettings;
            var existingStart = existing.Shift.GetAbsoluteStart(existingEs);
            var existingEnd = existing.Shift.GetAbsoluteEnd(existingEs);

            if (targetStart < existingEnd && targetEnd > existingStart)
            {
                return $"Time conflict with '{existing.Shift.Title}' ({existing.Status}).";
            }
        }

        return null;
    }

    private async Task<string?> CheckEeCapAsync(EventSettings es, int dayOffset)
    {
        var availableSlots = _eventSettingsService.GetAvailableEeSlots(es, dayOffset);
        if (availableSlots <= 0)
            return "Early entry capacity reached for this day.";

        var currentEeCount = await _dbContext.DutySignups
            .Where(d => d.Status == SignupStatus.Confirmed &&
                        d.Shift.Rota.EventSettingsId == es.Id &&
                        d.Shift.DayOffset < 0)
            .Select(d => d.UserId)
            .Distinct()
            .CountAsync();

        if (currentEeCount >= availableSlots)
            return "Early entry capacity reached.";

        return null;
    }

    private async Task<bool> IsPrivilegedAsync(Guid userId, Guid departmentTeamId)
    {
        return await _authService.CanApproveSignupsAsync(userId, departmentTeamId);
    }
}
