using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Humans.Web.Controllers;

[Authorize]
[Route("Teams/{slug}/Shifts")]
public class ShiftAdminController : Controller
{
    private readonly ITeamService _teamService;
    private readonly IShiftManagementService _shiftMgmt;
    private readonly IShiftSignupService _signupService;
    private readonly IProfileService _profileService;
    private readonly UserManager<User> _userManager;
    private readonly IClock _clock;
    private readonly ILogger<ShiftAdminController> _logger;

    public ShiftAdminController(
        ITeamService teamService,
        IShiftManagementService shiftMgmt,
        IShiftSignupService signupService,
        IProfileService profileService,
        UserManager<User> userManager,
        IClock clock,
        ILogger<ShiftAdminController> logger)
    {
        _teamService = teamService;
        _shiftMgmt = shiftMgmt;
        _signupService = signupService;
        _profileService = profileService;
        _userManager = userManager;
        _clock = clock;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string slug)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();

        var canManage = await CanManageAsync(userId.Value, team.Id);
        var canApprove = await CanApproveAsync(userId.Value, team.Id);
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

        // Batch-load volunteer event profiles for signup display
        var allUserIds = rotas.SelectMany(r => r.Shifts)
            .SelectMany(s => s.ShiftSignups)
            .Select(su => su.UserId)
            .Distinct()
            .ToList();

        var canViewMedical = User.IsInRole(RoleNames.NoInfoAdmin) || User.IsInRole(RoleNames.Admin);
        var profileDict = new Dictionary<Guid, VolunteerEventProfile>();
        foreach (var uid in allUserIds)
        {
            var profile = await _profileService.GetShiftProfileAsync(uid, includeMedical: canViewMedical);
            if (profile != null)
                profileDict[uid] = profile;
        }

        var staffingData = await _shiftMgmt.GetStaffingDataAsync(es.Id, team.Id);

        var model = new ShiftAdminViewModel
        {
            Department = team,
            EventSettings = es,
            Rotas = rotas.ToList(),
            PendingSignups = pendingSignups,
            TotalSlots = totalSlots,
            ConfirmedCount = confirmedCount,
            CanManageShifts = canManage,
            CanApproveSignups = canApprove,
            VolunteerProfiles = profileDict,
            CanViewMedical = canViewMedical,
            StaffingData = staffingData.ToList(),
            Now = _clock.GetCurrentInstant()
        };

        return View(model);
    }

    [HttpPost("Rotas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRota(string slug, CreateRotaModel model)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await CanManageAsync(userId.Value, team.Id)) return Forbid();

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
        if (!await CanManageAsync(userId.Value, team.Id)) return Forbid();

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
        if (!await CanManageAsync(userId.Value, team.Id)) return Forbid();

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
        if (!await CanManageAsync(userId.Value, team.Id)) return Forbid();

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

    [HttpPost("Rotas/{rotaId}/Deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateRota(string slug, Guid rotaId)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await CanManageAsync(userId.Value, team.Id)) return Forbid();

        await _shiftMgmt.DeactivateRotaAsync(rotaId);
        TempData["SuccessMessage"] = "Rota deactivated.";
        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpPost("Shifts/{shiftId}/Deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateShift(string slug, Guid shiftId)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await CanManageAsync(userId.Value, team.Id)) return Forbid();

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
        if (!await CanApproveAsync(userId.Value, team.Id)) return Forbid();

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
        if (!await CanApproveAsync(userId.Value, team.Id)) return Forbid();

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
        if (!await CanApproveAsync(userId.Value, team.Id)) return Forbid();

        var signupCheck = await _signupService.GetByIdAsync(signupId);
        if (signupCheck == null) return NotFound();
        if (signupCheck.Shift.Rota.TeamId != team.Id) return NotFound();

        var result = await _signupService.MarkNoShowAsync(signupId, userId.Value);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Marked as no-show." : result.Error;

        return RedirectToAction(nameof(Index), new { slug });
    }

    [HttpGet("SearchVolunteers")]
    public async Task<IActionResult> SearchVolunteers(string slug, Guid shiftId, string? query)
    {
        var (team, userId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || userId == null) return NotFound();
        if (!await CanApproveAsync(userId.Value, team.Id)) return Forbid();

        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Json(Array.Empty<VolunteerSearchResult>());

        try
        {
            var shift = await _shiftMgmt.GetShiftByIdAsync(shiftId);
            if (shift == null) return NotFound();
            if (shift.Rota.TeamId != team.Id) return NotFound();

            var es = shift.Rota.EventSettings ?? await _shiftMgmt.GetActiveAsync();
            if (es == null) return NotFound();

            var shiftStart = shift.GetAbsoluteStart(es);
            var shiftEnd = shift.GetAbsoluteEnd(es);

            var users = await _userManager.Users
                .Where(u => EF.Functions.ILike(u.DisplayName, "%" + query + "%"))
                .Take(10)
                .ToListAsync();

            var canViewMedical = User.IsInRole(RoleNames.NoInfoAdmin) || User.IsInRole(RoleNames.Admin);

            var results = new List<VolunteerSearchResult>();
            foreach (var user in users)
            {
                var profile = await _profileService.GetShiftProfileAsync(user.Id, includeMedical: canViewMedical);
                var userSignups = await _signupService.GetByUserAsync(user.Id, es.Id);
                var confirmed = userSignups.Where(s => s.Status == SignupStatus.Confirmed).ToList();

                var hasOverlap = confirmed.Any(s =>
                {
                    var sStart = s.Shift.GetAbsoluteStart(es);
                    var sEnd = s.Shift.GetAbsoluteEnd(es);
                    return shiftStart < sEnd && shiftEnd > sStart;
                });

                results.Add(new VolunteerSearchResult
                {
                    UserId = user.Id,
                    DisplayName = user.DisplayName,
                    Skills = profile?.Skills ?? [],
                    Quirks = profile?.Quirks ?? [],
                    Languages = profile?.Languages ?? [],
                    DietaryPreference = profile?.DietaryPreference,
                    BookedShiftCount = confirmed.Count,
                    HasOverlap = hasOverlap,
                    MedicalConditions = profile?.MedicalConditions
                });
            }

            return Json(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Volunteer search failed for shift {ShiftId}, query '{Query}'", shiftId, query);
            return StatusCode(500, new { error = "Search failed." });
        }
    }

    [HttpPost("Voluntell")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Voluntell(string slug, Guid shiftId, Guid userId)
    {
        var (team, currentUserId) = await ResolveTeamAndUserAsync(slug);
        if (team == null || currentUserId == null) return NotFound();
        if (!await CanApproveAsync(currentUserId.Value, team.Id)) return Forbid();

        var shift = await _shiftMgmt.GetShiftByIdAsync(shiftId);
        if (shift == null) return NotFound();
        if (shift.Rota.TeamId != team.Id) return NotFound();

        var result = await _signupService.VoluntellAsync(userId, shiftId, currentUserId.Value);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Volunteer assigned to shift." : result.Error;

        return RedirectToAction(nameof(Index), new { slug });
    }

    private async Task<bool> CanManageAsync(Guid userId, Guid teamId)
    {
        return User.IsInRole(RoleNames.Admin) ||
               await _shiftMgmt.CanManageShiftsAsync(userId, teamId);
    }

    private async Task<bool> CanApproveAsync(Guid userId, Guid teamId)
    {
        return User.IsInRole(RoleNames.Admin) ||
               User.IsInRole(RoleNames.NoInfoAdmin) ||
               await _shiftMgmt.CanApproveSignupsAsync(userId, teamId);
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
