using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Humans.Application.Interfaces;
using Humans.Web.Extensions;
using Humans.Web.Models;

namespace Humans.Web.Controllers;

[Authorize(Roles = "Board,Admin")]
[Route("Board")]
public class BoardController : Controller
{
    private readonly IAuditLogService _auditLogService;
    private readonly IOnboardingService _onboardingService;

    public BoardController(
        IAuditLogService auditLogService,
        IOnboardingService onboardingService)
    {
        _auditLogService = auditLogService;
        _onboardingService = onboardingService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var dashboardData = await _onboardingService.GetAdminDashboardAsync();
        var recentEntries = await _auditLogService.GetRecentAsync(15);

        var viewModel = new AdminDashboardViewModel
        {
            TotalMembers = dashboardData.TotalMembers,
            ActiveMembers = dashboardData.ActiveMembers,
            PendingVolunteers = dashboardData.PendingVolunteers,
            PendingApplications = dashboardData.PendingApplications,
            PendingConsents = dashboardData.PendingConsents,
            RecentActivity = recentEntries.Select(e => new RecentActivityViewModel
            {
                Description = e.Description,
                Timestamp = e.OccurredAt.ToDateTimeUtc(),
                Type = e.Action.ToString()
            }).ToList(),
            TotalApplications = dashboardData.TotalApplications,
            ApprovedApplications = dashboardData.ApprovedApplications,
            RejectedApplications = dashboardData.RejectedApplications,
            ColaboradorApplied = dashboardData.ColaboradorApplied,
            AsociadoApplied = dashboardData.AsociadoApplied
        };

        return View(viewModel);
    }

}
