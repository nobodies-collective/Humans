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
using NodaTime.Text;

namespace Humans.Web.Controllers;

[Authorize]
[Route("Shifts/Dashboard")]
public class ShiftDashboardController : Controller
{
    private readonly IShiftManagementService _shiftMgmt;
    private readonly IShiftSignupService _signupService;
    private readonly IProfileService _profileService;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<ShiftDashboardController> _logger;

    public ShiftDashboardController(
        IShiftManagementService shiftMgmt,
        IShiftSignupService signupService,
        IProfileService profileService,
        UserManager<User> userManager,
        ILogger<ShiftDashboardController> logger)
    {
        _shiftMgmt = shiftMgmt;
        _signupService = signupService;
        _profileService = profileService;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? departmentId, string? date)
    {
        if (!User.IsInRole(RoleNames.NoInfoAdmin) && !User.IsInRole(RoleNames.Admin))
            return Forbid();

        var es = await _shiftMgmt.GetActiveAsync();
        if (es == null)
        {
            TempData["ErrorMessage"] = "No active event settings configured.";
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        LocalDate? filterDate = null;
        if (!string.IsNullOrEmpty(date))
        {
            var parseResult = LocalDatePattern.Iso.Parse(date);
            if (parseResult.Success)
                filterDate = parseResult.Value;
        }

        var shifts = await _shiftMgmt.GetUrgentShiftsAsync(es.Id, limit: null, departmentId, filterDate);
        var staffingData = await _shiftMgmt.GetStaffingDataAsync(es.Id, departmentId);

        var deptTuples = await _shiftMgmt.GetDepartmentsWithRotasAsync(es.Id);
        var departments = deptTuples.Select(d => new DepartmentOption
        {
            TeamId = d.TeamId,
            Name = d.TeamName
        }).ToList();

        var model = new ShiftDashboardViewModel
        {
            Shifts = shifts.ToList(),
            Departments = departments,
            SelectedDepartmentId = departmentId,
            SelectedDate = date,
            EventSettings = es,
            StaffingData = staffingData.ToList()
        };

        return View(model);
    }

    [HttpGet("SearchVolunteers")]
    public async Task<IActionResult> SearchVolunteers(Guid shiftId, string? query)
    {
        if (!User.IsInRole(RoleNames.NoInfoAdmin) && !User.IsInRole(RoleNames.Admin))
            return Forbid();

        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Json(Array.Empty<VolunteerSearchResult>());

        try
        {
            var shift = await _shiftMgmt.GetShiftByIdAsync(shiftId);
            if (shift == null) return NotFound();

            var es = shift.Rota.EventSettings ?? await _shiftMgmt.GetActiveAsync();
            if (es == null) return NotFound();

            var results = await BuildVolunteerSearchResultsAsync(shift, query, es);
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
    public async Task<IActionResult> Voluntell(Guid shiftId, Guid userId)
    {
        if (!User.IsInRole(RoleNames.NoInfoAdmin) && !User.IsInRole(RoleNames.Admin))
            return Forbid();

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return Unauthorized();

        var result = await _signupService.VoluntellAsync(userId, shiftId, currentUser.Id);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Volunteer assigned to shift." : result.Error;

        return RedirectToAction(nameof(Index));
    }

    private async Task<List<VolunteerSearchResult>> BuildVolunteerSearchResultsAsync(
        Shift shift, string query, EventSettings es)
    {
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
            var confirmedSignups = userSignups.Where(s => s.Status == SignupStatus.Confirmed).ToList();

            var hasOverlap = confirmedSignups.Any(s =>
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
                BookedShiftCount = confirmedSignups.Count,
                HasOverlap = hasOverlap,
                MedicalConditions = profile?.MedicalConditions
            });
        }

        return results;
    }
}
