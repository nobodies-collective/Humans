using System.Text.Json;
using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;

namespace Humans.Web.Controllers;

[Authorize]
[Route("Shifts")]
public class ShiftsController : Controller
{
    private readonly IEventSettingsService _eventSettingsService;
    private readonly IDutySignupService _dutySignupService;
    private readonly IShiftUrgencyService _urgencyService;
    private readonly IShiftAuthorizationService _authService;
    private readonly UserManager<User> _userManager;
    private readonly IClock _clock;

    public ShiftsController(
        IEventSettingsService eventSettingsService,
        IDutySignupService dutySignupService,
        IShiftUrgencyService urgencyService,
        IShiftAuthorizationService authService,
        UserManager<User> userManager,
        IClock clock)
    {
        _eventSettingsService = eventSettingsService;
        _dutySignupService = dutySignupService;
        _urgencyService = urgencyService;
        _authService = authService;
        _userManager = userManager;
        _clock = clock;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? departmentId, string? date, bool showFull = false)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var es = await _eventSettingsService.GetActiveAsync();
        if (es == null) return View("NoActiveEvent");

        var isPrivileged = User.IsInRole(RoleNames.Admin) ||
                           User.IsInRole(RoleNames.NoInfoAdmin) ||
                           (await _authService.GetCoordinatorDepartmentIdsAsync(user.Id)).Count > 0;

        var userSignups = await _dutySignupService.GetByUserAsync(user.Id, es.Id);
        var hasSignups = userSignups.Count > 0;

        if (!es.IsShiftBrowsingOpen && !isPrivileged && !hasSignups)
            return View("BrowsingClosed");

        // Build the browse view from urgency service data
        var urgentShifts = await _urgencyService.GetUrgentShiftsAsync(es.Id, departmentId: departmentId);

        var userSignupShiftIds = userSignups
            .Where(s => s.Status is SignupStatus.Confirmed or SignupStatus.Pending)
            .Select(s => s.ShiftId)
            .ToHashSet();
        var userSignupStatuses = userSignups
            .Where(s => s.Status is SignupStatus.Confirmed or SignupStatus.Pending)
            .ToDictionary(s => s.ShiftId, s => s.Status);

        var model = new ShiftBrowseViewModel
        {
            EventSettings = es,
            FilterDepartmentId = departmentId,
            FilterDate = date,
            ShowFullShifts = showFull,
            UserSignupShiftIds = userSignupShiftIds,
            UserSignupStatuses = userSignupStatuses
        };

        return View(model);
    }

    [HttpPost("SignUp")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignUp(Guid shiftId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _dutySignupService.SignUpAsync(user.Id, shiftId);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = result.Warning != null
            ? $"Signed up successfully. Note: {result.Warning}"
            : "Signed up successfully!";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Bail")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bail(Guid signupId, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _dutySignupService.BailAsync(signupId, user.Id, reason);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Error;
            return RedirectToAction(nameof(Mine));
        }

        TempData["SuccessMessage"] = "Successfully bailed from shift.";
        return RedirectToAction(nameof(Mine));
    }

    [HttpGet("Mine")]
    public async Task<IActionResult> Mine()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var es = await _eventSettingsService.GetActiveAsync();

        var signups = es != null
            ? await _dutySignupService.GetByUserAsync(user.Id, es.Id)
            : [];

        var now = _clock.GetCurrentInstant();
        var model = new MyShiftsViewModel { EventSettings = es };

        foreach (var signup in signups)
        {
            var shiftEs = signup.Shift.Rota.EventSettings;
            var item = new MySignupItem
            {
                Signup = signup,
                DepartmentName = signup.Shift.Rota.Team.Name,
                AbsoluteStart = signup.Shift.GetAbsoluteStart(shiftEs),
                AbsoluteEnd = signup.Shift.GetAbsoluteEnd(shiftEs)
            };

            switch (signup.Status)
            {
                case SignupStatus.Confirmed when item.AbsoluteStart > now:
                    model.Upcoming.Add(item);
                    break;
                case SignupStatus.Pending:
                    model.Pending.Add(item);
                    break;
                default:
                    model.Past.Add(item);
                    break;
            }
        }

        model.Upcoming = model.Upcoming.OrderBy(s => s.AbsoluteStart).ToList();
        model.Pending = model.Pending.OrderBy(s => s.AbsoluteStart).ToList();
        model.Past = model.Past.OrderByDescending(s => s.AbsoluteStart).ToList();

        // iCal URL
        if (user.ICalToken == null)
        {
            user.ICalToken = Guid.NewGuid();
            await _userManager.UpdateAsync(user);
        }
        model.ICalUrl = Url.Action("ICalFeed", "Shifts", new { token = user.ICalToken }, Request.Scheme);

        return View(model);
    }

    [HttpPost("Mine/RegenerateIcal")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateIcal()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        user.ICalToken = Guid.NewGuid();
        await _userManager.UpdateAsync(user);

        TempData["SuccessMessage"] = "iCal URL regenerated.";
        return RedirectToAction(nameof(Mine));
    }

    [HttpGet("Settings")]
    public async Task<IActionResult> Settings()
    {
        if (!User.IsInRole(RoleNames.Admin))
            return Forbid();

        var es = await _eventSettingsService.GetActiveAsync();
        var model = new EventSettingsViewModel();

        if (es != null)
        {
            model.Id = es.Id;
            model.EventName = es.EventName;
            model.TimeZoneId = es.TimeZoneId;
            model.GateOpeningDate = LocalDatePattern.Iso.Format(es.GateOpeningDate);
            model.BuildStartOffset = es.BuildStartOffset;
            model.EventEndOffset = es.EventEndOffset;
            model.StrikeEndOffset = es.StrikeEndOffset;
            model.EarlyEntryCapacityJson = JsonSerializer.Serialize(es.EarlyEntryCapacity);
            model.BarriosEarlyEntryAllocationJson = es.BarriosEarlyEntryAllocation != null
                ? JsonSerializer.Serialize(es.BarriosEarlyEntryAllocation)
                : null;
            model.EarlyEntryClose = es.EarlyEntryClose.HasValue
                ? InstantPattern.General.Format(es.EarlyEntryClose.Value)
                : null;
            model.IsShiftBrowsingOpen = es.IsShiftBrowsingOpen;
            model.GlobalVolunteerCap = es.GlobalVolunteerCap;
            model.ReminderLeadTimeHours = es.ReminderLeadTimeHours;
            model.IsActive = es.IsActive;
        }

        return View(model);
    }

    [HttpPost("Settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(EventSettingsViewModel model)
    {
        if (!User.IsInRole(RoleNames.Admin))
            return Forbid();

        if (!ModelState.IsValid)
            return View(model);

        var parsedDate = LocalDatePattern.Iso.Parse(model.GateOpeningDate);
        if (!parsedDate.Success)
        {
            ModelState.AddModelError(nameof(model.GateOpeningDate), "Invalid date format.");
            return View(model);
        }

        Instant? earlyEntryClose = null;
        if (!string.IsNullOrEmpty(model.EarlyEntryClose))
        {
            var parsedInstant = InstantPattern.General.Parse(model.EarlyEntryClose);
            if (parsedInstant.Success)
                earlyEntryClose = parsedInstant.Value;
        }

        var eeCapacity = !string.IsNullOrEmpty(model.EarlyEntryCapacityJson)
            ? JsonSerializer.Deserialize<Dictionary<int, int>>(model.EarlyEntryCapacityJson) ?? new()
            : new Dictionary<int, int>();

        Dictionary<int, int>? barriosAllocation = null;
        if (!string.IsNullOrEmpty(model.BarriosEarlyEntryAllocationJson))
            barriosAllocation = JsonSerializer.Deserialize<Dictionary<int, int>>(model.BarriosEarlyEntryAllocationJson);

        if (model.Id.HasValue)
        {
            var existing = await _eventSettingsService.GetByIdAsync(model.Id.Value);
            if (existing == null) return NotFound();

            existing.EventName = model.EventName;
            existing.TimeZoneId = model.TimeZoneId;
            existing.GateOpeningDate = parsedDate.Value;
            existing.BuildStartOffset = model.BuildStartOffset;
            existing.EventEndOffset = model.EventEndOffset;
            existing.StrikeEndOffset = model.StrikeEndOffset;
            existing.EarlyEntryCapacity = eeCapacity;
            existing.BarriosEarlyEntryAllocation = barriosAllocation;
            existing.EarlyEntryClose = earlyEntryClose;
            existing.IsShiftBrowsingOpen = model.IsShiftBrowsingOpen;
            existing.GlobalVolunteerCap = model.GlobalVolunteerCap;
            existing.ReminderLeadTimeHours = model.ReminderLeadTimeHours;
            existing.IsActive = model.IsActive;

            await _eventSettingsService.UpdateAsync(existing);
        }
        else
        {
            var entity = new EventSettings
            {
                Id = Guid.NewGuid(),
                EventName = model.EventName,
                TimeZoneId = model.TimeZoneId,
                GateOpeningDate = parsedDate.Value,
                BuildStartOffset = model.BuildStartOffset,
                EventEndOffset = model.EventEndOffset,
                StrikeEndOffset = model.StrikeEndOffset,
                EarlyEntryCapacity = eeCapacity,
                BarriosEarlyEntryAllocation = barriosAllocation,
                EarlyEntryClose = earlyEntryClose,
                IsShiftBrowsingOpen = model.IsShiftBrowsingOpen,
                GlobalVolunteerCap = model.GlobalVolunteerCap,
                ReminderLeadTimeHours = model.ReminderLeadTimeHours,
                IsActive = model.IsActive,
                CreatedAt = _clock.GetCurrentInstant()
            };

            await _eventSettingsService.CreateAsync(entity);
        }

        TempData["SuccessMessage"] = "Event settings saved.";
        return RedirectToAction(nameof(Settings));
    }
}
