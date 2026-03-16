using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Web.Models;

namespace Humans.Web.Controllers;

public class HomeController : Controller
{
    private readonly UserManager<User> _userManager;
    private readonly IMembershipCalculator _membershipCalculator;
    private readonly IProfileService _profileService;
    private readonly IApplicationDecisionService _applicationDecisionService;
    private readonly IShiftManagementService _shiftMgmt;
    private readonly IShiftSignupService _shiftSignup;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public HomeController(
        UserManager<User> userManager,
        IMembershipCalculator membershipCalculator,
        IProfileService profileService,
        IApplicationDecisionService applicationDecisionService,
        IShiftManagementService shiftMgmt,
        IShiftSignupService shiftSignup,
        IConfiguration configuration,
        IClock clock)
    {
        _userManager = userManager;
        _membershipCalculator = membershipCalculator;
        _profileService = profileService;
        _applicationDecisionService = applicationDecisionService;
        _shiftMgmt = shiftMgmt;
        _shiftSignup = shiftSignup;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<IActionResult> Index()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return View();
        }

        // Show dashboard for logged in users
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return View();
        }

        var profile = await _profileService.GetProfileAsync(user.Id);

        var membershipSnapshot = await _membershipCalculator.GetMembershipSnapshotAsync(user.Id);

        // Get all applications for the user
        var applications = await _applicationDecisionService.GetUserApplicationsAsync(user.Id);
        var latestApplication = applications.Count > 0 ? applications[0] : null;

        var hasPendingApp = latestApplication != null &&
            latestApplication.Status == ApplicationStatus.Submitted;

        // Get term expiry from latest approved application for the user's current tier
        var currentTier = profile?.MembershipTier ?? MembershipTier.Volunteer;
        DateTime? termExpiresAt = null;
        var termExpiresSoon = false;
        var termExpired = false;

        if (currentTier != MembershipTier.Volunteer)
        {
            var latestApprovedApp = applications
                .Where(a => a.Status == ApplicationStatus.Approved
                    && a.MembershipTier == currentTier
                    && a.TermExpiresAt != null)
                .OrderByDescending(a => a.TermExpiresAt)
                .FirstOrDefault();

            if (latestApprovedApp?.TermExpiresAt != null)
            {
                var today = _clock.GetCurrentInstant().InUtc().Date;
                var expiryDate = latestApprovedApp.TermExpiresAt.Value;
                termExpiresAt = expiryDate.AtMidnight().InUtc().ToDateTimeUtc();
                termExpired = expiryDate < today;
                termExpiresSoon = !termExpired && expiryDate <= today.PlusDays(90);
            }
        }

        var viewModel = new DashboardViewModel
        {
            DisplayName = user.DisplayName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            MembershipStatus = membershipSnapshot.Status.ToString(),
            HasProfile = profile != null,
            ProfileComplete = profile != null && !string.IsNullOrEmpty(profile.FirstName),
            PendingConsents = membershipSnapshot.PendingConsentCount,
            TotalRequiredConsents = membershipSnapshot.RequiredConsentCount,
            IsVolunteerMember = membershipSnapshot.IsVolunteerMember,
            MembershipTier = currentTier,
            ConsentCheckStatus = profile?.ConsentCheckStatus,
            IsRejected = profile?.RejectedAt != null,
            RejectionReason = profile?.RejectionReason,
            HasPendingApplication = hasPendingApp,
            LatestApplicationStatus = latestApplication?.Status.ToString(),
            LatestApplicationDate = latestApplication?.SubmittedAt.ToDateTimeUtc(),
            LatestApplicationTier = latestApplication?.MembershipTier,
            TermExpiresAt = termExpiresAt,
            TermExpiresSoon = termExpiresSoon,
            TermExpired = termExpired,
            MemberSince = user.CreatedAt.ToDateTimeUtc(),
            LastLogin = user.LastLoginAt?.ToDateTimeUtc()
        };

        // Build shift cards if there's an active event
        var activeEvent = await _shiftMgmt.GetActiveAsync();
        if (activeEvent != null)
        {
            var userSignups = await _shiftSignup.GetByUserAsync(user.Id, activeEvent.Id);
            var hasSignups = userSignups.Count > 0;

            if (activeEvent.IsShiftBrowsingOpen || hasSignups)
            {
                var now = _clock.GetCurrentInstant();
                var nextShifts = userSignups
                    .Where(s => s.Status == SignupStatus.Confirmed)
                    .Select(s =>
                    {
                        var es = s.Shift.Rota.EventSettings;
                        return new MySignupItem
                        {
                            Signup = s,
                            DepartmentName = s.Shift.Rota.Team.Name,
                            AbsoluteStart = s.Shift.GetAbsoluteStart(es),
                            AbsoluteEnd = s.Shift.GetAbsoluteEnd(es)
                        };
                    })
                    .Where(i => i.AbsoluteEnd > now)
                    .OrderBy(i => i.AbsoluteStart)
                    .Take(3)
                    .ToList();

                var pendingCount = userSignups.Count(s => s.Status == SignupStatus.Pending);

                var urgentShifts = await _shiftMgmt.GetUrgentShiftsAsync(activeEvent.Id, limit: 3);
                var urgentItems = urgentShifts.Select(u =>
                {
                    var es = u.Shift.Rota.EventSettings;
                    return new UrgentShiftItem
                    {
                        Shift = u.Shift,
                        DepartmentName = u.DepartmentName,
                        AbsoluteStart = u.Shift.GetAbsoluteStart(es),
                        RemainingSlots = u.RemainingSlots,
                        UrgencyScore = u.UrgencyScore
                    };
                }).ToList();

                var shiftCards = new ShiftCardsViewModel
                {
                    NextShifts = nextShifts,
                    PendingCount = pendingCount,
                    UrgentShifts = urgentItems
                };

                ViewData["ShiftCards"] = shiftCards;
            }
        }

        return View("Dashboard", viewModel);
    }

    public IActionResult Privacy()
    {
        ViewData["DpoEmail"] = _configuration["Email:DpoAddress"];
        return View();
    }

    public IActionResult About()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Route("/Home/Error/{statusCode?}")]
    public IActionResult Error(int? statusCode = null)
    {
        if (statusCode == 404)
        {
            return View("Error404");
        }

        return View();
    }
}
