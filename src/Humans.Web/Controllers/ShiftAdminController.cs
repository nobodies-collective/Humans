using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace Humans.Web.Controllers;

[Authorize]
[Route("Teams/{slug}/Shifts")]
public class ShiftAdminController : Controller
{
    private readonly ITeamService _teamService;
    private readonly IShiftManagementService _shiftMgmt;
    private readonly IShiftSignupService _signupService;
    private readonly UserManager<User> _userManager;
    private readonly IClock _clock;

    public ShiftAdminController(
        ITeamService teamService,
        IShiftManagementService shiftMgmt,
        IShiftSignupService signupService,
        UserManager<User> userManager,
        IClock clock)
    {
        _teamService = teamService;
        _shiftMgmt = shiftMgmt;
        _signupService = signupService;
        _userManager = userManager;
        _clock = clock;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string slug)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();

        var canManage = await _shiftMgmt.CanManageShiftsAsync(userId.Value, team.Id);
        var canApprove = await _shiftMgmt.CanApproveSignupsAsync(userId.Value, team.Id);
        if (!canManage && !canApprove) return Forbid();

        var es = await _shiftMgmt.GetActiveAsync();
        if (es == null)
        {
            TempData["ErrorMessage"] = "No active event settings configured.";
            return RedirectToAction("Details", "Team", new { slug });
        }

        var rotas = await _shiftMgmt.GetRotasByDepartmentAsync(team.Id, es.Id);
        var pendingSignups = new List<ShiftSignup>();
        var totalSlots = 0;
        var confirmedCount = 0;

        foreach (var rota in rotas)
        {
            foreach (var shift in rota.Shifts.Where(s => s.IsActive))
            {
                totalSlots += shift.MaxVolunteers;
                var shiftSignups = await _signupService.GetByShiftAsync(shift.Id);
                confirmedCount += shiftSignups.Count(s => s.Status == SignupStatus.Confirmed);
                pendingSignups.AddRange(shiftSignups.Where(s => s.Status == SignupStatus.Pending));
            }
        }

        var model = new ShiftAdminViewModel
        {
            Department = team,
            EventSettings = es,
            Rotas = rotas.ToList(),
            PendingSignups = pendingSignups,
            TotalSlots = totalSlots,
            ConfirmedCount = confirmedCount,
            CanManageShifts = canManage,
            CanApproveSignups = canApprove
        };

        return View(model);
    }

    [HttpPost("Rotas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRota(string slug, CreateRotaModel model)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanManageShiftsAsync(userId.Value, team.Id)) return Forbid();

        var es = await _shiftMgmt.GetActiveAsync();
        if (es == null) return BadRequest("No active event.");

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please fix the errors below.";
            return RedirectToAction(nameof(Index), new { slug });
        }

        var rota = new Rota
        {
            Id = Guid.NewGuid(),
            EventSettingsId = es.Id,
            TeamId = team.Id,
            Name = model.Name,
            Description = model.Description,
            Priority = model.Priority,
            Policy = model.Policy,
            CreatedAt = _clock.GetCurrentInstant()
        };

        await _shiftMgmt.CreateRotaAsync(rota);
        TempData["SuccessMessage"] = $"Rota '{model.Name}' created.";
        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Rotas/{rotaId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRota(string slug, Guid rotaId, EditRotaModel model)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanManageShiftsAsync(userId.Value, team.Id)) return Forbid();

        var rota = await _shiftMgmt.GetRotaByIdAsync(rotaId);
        if (rota == null) return NotFound();
        if (rota.TeamId != team.Id) return NotFound();

        rota.Name = model.Name;
        rota.Description = model.Description;
        rota.Priority = model.Priority;
        rota.Policy = model.Policy;
        rota.IsActive = model.IsActive;

        await _shiftMgmt.UpdateRotaAsync(rota);
        TempData["SuccessMessage"] = $"Rota '{model.Name}' updated.";
        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Shifts")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShift(string slug, CreateShiftModel model)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanManageShiftsAsync(userId.Value, team.Id)) return Forbid();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please fix the errors below.";
            return RedirectToAction(nameof(Index), new { slug });
        }

        // Verify rota belongs to this department
        var rota = await _shiftMgmt.GetRotaByIdAsync(model.RotaId);
        if (rota == null) return NotFound();
        if (rota.TeamId != team.Id) return NotFound();

        if (!TimeOnly.TryParse(model.StartTime, System.Globalization.CultureInfo.InvariantCulture, out var parsedTime))
        {
            TempData["ErrorMessage"] = "Invalid start time format.";
            return RedirectToAction(nameof(Index), new { slug });
        }

        var shift = new Shift
        {
            Id = Guid.NewGuid(),
            RotaId = model.RotaId,
            Title = model.Title,
            Description = model.Description,
            DayOffset = model.DayOffset,
            StartTime = new LocalTime(parsedTime.Hour, parsedTime.Minute),
            Duration = Duration.FromHours(model.DurationHours),
            MinVolunteers = model.MinVolunteers,
            MaxVolunteers = model.MaxVolunteers,
            AdminOnly = model.AdminOnly,
            CreatedAt = _clock.GetCurrentInstant()
        };

        try
        {
            await _shiftMgmt.CreateShiftAsync(shift);
            TempData["SuccessMessage"] = $"Shift '{model.Title}' created.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Shifts/{shiftId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditShift(string slug, Guid shiftId, EditShiftModel model)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanManageShiftsAsync(userId.Value, team.Id)) return Forbid();

        var shift = await _shiftMgmt.GetShiftByIdAsync(shiftId);
        if (shift == null) return NotFound();
        if (shift.Rota.TeamId != team.Id) return NotFound();

        if (!TimeOnly.TryParse(model.StartTime, System.Globalization.CultureInfo.InvariantCulture, out var parsedTime))
        {
            TempData["ErrorMessage"] = "Invalid start time format.";
            return RedirectToAction(nameof(Index), new { slug });
        }

        shift.Title = model.Title;
        shift.Description = model.Description;
        shift.DayOffset = model.DayOffset;
        shift.StartTime = new LocalTime(parsedTime.Hour, parsedTime.Minute);
        shift.Duration = Duration.FromHours(model.DurationHours);
        shift.MinVolunteers = model.MinVolunteers;
        shift.MaxVolunteers = model.MaxVolunteers;
        shift.AdminOnly = model.AdminOnly;
        shift.IsActive = model.IsActive;

        await _shiftMgmt.UpdateShiftAsync(shift);
        TempData["SuccessMessage"] = $"Shift '{model.Title}' updated.";
        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Shifts/{shiftId}/Deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateShift(string slug, Guid shiftId)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanManageShiftsAsync(userId.Value, team.Id)) return Forbid();

        var shift = await _shiftMgmt.GetShiftByIdAsync(shiftId);
        if (shift == null) return NotFound();
        if (shift.Rota.TeamId != team.Id) return NotFound();

        await _shiftMgmt.DeactivateShiftAsync(shiftId);
        TempData["SuccessMessage"] = "Shift deactivated.";
        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Signups/{signupId}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveSignup(string slug, Guid signupId)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanApproveSignupsAsync(userId.Value, team.Id)) return Forbid();

        var signup = await _signupService.GetByIdAsync(signupId);
        if (signup == null) return NotFound();
        if (signup.Shift.Rota.TeamId != team.Id) return NotFound();

        var result = await _signupService.ApproveAsync(signupId, userId.Value);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? (result.Warning ?? "Signup approved.") : result.Error;

        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Signups/{signupId}/Refuse")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefuseSignup(string slug, Guid signupId, string? reason)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanApproveSignupsAsync(userId.Value, team.Id)) return Forbid();

        var signup = await _signupService.GetByIdAsync(signupId);
        if (signup == null) return NotFound();
        if (signup.Shift.Rota.TeamId != team.Id) return NotFound();

        var result = await _signupService.RefuseAsync(signupId, userId.Value, reason);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Signup refused." : result.Error;

        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Signups/{signupId}/NoShow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNoShow(string slug, Guid signupId)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await _shiftMgmt.CanApproveSignupsAsync(userId.Value, team.Id)) return Forbid();

        var signupCheck = await _signupService.GetByIdAsync(signupId);
        if (signupCheck == null) return NotFound();
        if (signupCheck.Shift.Rota.TeamId != team.Id) return NotFound();

        var result = await _signupService.MarkNoShowAsync(signupId, userId.Value);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Marked as no-show." : result.Error;

        return RedirectToAction(nameof(Index), new { slug });
    }

    private async Task<(Team? Team, Guid? UserId)> ResolveTeamAndUserAsync(string slug)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return (null, null);

        var team = await _teamService.GetTeamBySlugAsync(slug);
        if (team == null || team.ParentTeamId != null || team.SystemTeamType != SystemTeamType.None)
            return (null, null);

        return (team, user.Id);
    }
}
