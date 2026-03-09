using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;
using Humans.Application.DTOs;
using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Configuration;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Humans.Web.Extensions;
using Humans.Web.Models;

namespace Humans.Web.Controllers;

[Authorize(Roles = "Board,Admin")]
[Route("Admin")]
public class AdminController : Controller
{
    private readonly HumansDbContext _dbContext; // Only used by DbVersion (EF migration introspection)
    private readonly UserManager<User> _userManager;
    private readonly ITeamService _teamService;
    private readonly IGoogleSyncService _googleSyncService;
    private readonly IAuditLogService _auditLogService;
    private readonly IRoleAssignmentService _roleAssignmentService;
    private readonly IProfileService _profileService;
    private readonly IApplicationDecisionService _applicationDecisionService;
    private readonly ITeamResourceService _teamResourceService;
    private readonly IClock _clock;
    private readonly ILogger<AdminController> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IWebHostEnvironment _environment;
    private readonly IOnboardingService _onboardingService;

    public AdminController(
        HumansDbContext dbContext,
        UserManager<User> userManager,
        ITeamService teamService,
        IGoogleSyncService googleSyncService,
        IAuditLogService auditLogService,
        IRoleAssignmentService roleAssignmentService,
        IProfileService profileService,
        IApplicationDecisionService applicationDecisionService,
        ITeamResourceService teamResourceService,
        IClock clock,
        ILogger<AdminController> logger,
        IStringLocalizer<SharedResource> localizer,
        IWebHostEnvironment environment,
        IOnboardingService onboardingService)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _teamService = teamService;
        _googleSyncService = googleSyncService;
        _auditLogService = auditLogService;
        _roleAssignmentService = roleAssignmentService;
        _profileService = profileService;
        _applicationDecisionService = applicationDecisionService;
        _teamResourceService = teamResourceService;
        _clock = clock;
        _logger = logger;
        _localizer = localizer;
        _environment = environment;
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

    [HttpGet("Humans")]
    public async Task<IActionResult> Humans(string? search, string? filter, string sort = "name", string dir = "asc", int page = 1)
    {
        var pageSize = 20;
        var allRows = await _profileService.GetFilteredHumansAsync(search, filter);
        var totalCount = allRows.Count;

        // Materialize for flexible sorting (fine at ~500 users)
        var allMatching = allRows.Select(r => new AdminHumanViewModel
        {
            Id = r.UserId,
            Email = r.Email,
            DisplayName = r.DisplayName,
            ProfilePictureUrl = r.ProfilePictureUrl,
            CreatedAt = r.CreatedAt,
            LastLoginAt = r.LastLoginAt,
            HasProfile = r.HasProfile,
            IsApproved = r.IsApproved,
            MembershipStatus = r.MembershipStatus
        }).ToList();

        var ascending = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        IEnumerable<AdminHumanViewModel> sorted = sort?.ToLowerInvariant() switch
        {
            "joined" => ascending
                ? allMatching.OrderBy(m => m.CreatedAt)
                : allMatching.OrderByDescending(m => m.CreatedAt),
            "login" => ascending
                ? allMatching.OrderBy(m => m.LastLoginAt.HasValue ? 0 : 1).ThenBy(m => m.LastLoginAt)
                : allMatching.OrderBy(m => m.LastLoginAt.HasValue ? 0 : 1).ThenByDescending(m => m.LastLoginAt),
            "status" => ascending
                ? allMatching.OrderBy(m => m.MembershipStatus, StringComparer.OrdinalIgnoreCase)
                : allMatching.OrderByDescending(m => m.MembershipStatus, StringComparer.OrdinalIgnoreCase),
            _ => ascending
                ? allMatching.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                : allMatching.OrderByDescending(m => m.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        var members = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var viewModel = new AdminHumanListViewModel
        {
            Humans = members,
            SearchTerm = search,
            StatusFilter = filter,
            SortBy = sort?.ToLowerInvariant() ?? "name",
            SortDir = ascending ? "asc" : "desc",
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpGet("Humans/{id}")]
    public async Task<IActionResult> HumanDetail(Guid id)
    {
        var data = await _profileService.GetAdminHumanDetailAsync(id);
        if (data == null)
        {
            return NotFound();
        }

        var now = _clock.GetCurrentInstant();

        var viewModel = new AdminHumanDetailViewModel
        {
            UserId = data.User.Id,
            Email = data.User.Email ?? string.Empty,
            DisplayName = data.User.DisplayName,
            ProfilePictureUrl = data.User.ProfilePictureUrl,
            CreatedAt = data.User.CreatedAt.ToDateTimeUtc(),
            LastLoginAt = data.User.LastLoginAt?.ToDateTimeUtc(),
            IsSuspended = data.Profile?.IsSuspended ?? false,
            IsApproved = data.Profile?.IsApproved ?? false,
            HasProfile = data.Profile != null,
            AdminNotes = data.Profile?.AdminNotes,
            MembershipTier = data.Profile?.MembershipTier ?? MembershipTier.Volunteer,
            ConsentCheckStatus = data.Profile?.ConsentCheckStatus,
            IsRejected = data.Profile?.RejectedAt != null,
            RejectionReason = data.Profile?.RejectionReason,
            RejectedAt = data.Profile?.RejectedAt?.ToDateTimeUtc(),
            RejectedByName = data.RejectedByName,
            ApplicationCount = data.Applications.Count,
            ConsentCount = data.ConsentCount,
            Applications = data.Applications
                .Take(5)
                .Select(a => new AdminHumanApplicationViewModel
                {
                    Id = a.Id,
                    Status = a.Status.ToString(),
                    SubmittedAt = a.SubmittedAt.ToDateTimeUtc()
                }).ToList(),
            RoleAssignments = data.RoleAssignments.Select(ra => new AdminRoleAssignmentViewModel
            {
                Id = ra.Id,
                UserId = ra.UserId,
                RoleName = ra.RoleName,
                ValidFrom = ra.ValidFrom.ToDateTimeUtc(),
                ValidTo = ra.ValidTo?.ToDateTimeUtc(),
                Notes = ra.Notes,
                IsActive = ra.IsActive(now),
                CreatedByName = ra.CreatedByUser?.DisplayName,
                CreatedAt = ra.CreatedAt.ToDateTimeUtc()
            }).ToList(),
            AuditLog = data.AuditEntries.Select(e => new AuditLogEntryViewModel
            {
                Action = e.Action,
                Description = e.Description,
                OccurredAt = e.OccurredAt,
                ActorName = e.ActorName ?? string.Empty,
                IsSystemAction = e.IsSystemAction
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet("Applications")]
    public async Task<IActionResult> Applications(string? status, string? tier, int page = 1)
    {
        var pageSize = 20;
        var (items, totalCount) = await _applicationDecisionService.GetFilteredApplicationsAsync(
            status, tier, page, pageSize);

        var applications = items.Select(a => new AdminApplicationViewModel
        {
            Id = a.Id,
            UserId = a.UserId,
            UserEmail = a.User.Email ?? string.Empty,
            UserDisplayName = a.User.DisplayName,
            Status = a.Status.ToString(),
            StatusBadgeClass = a.Status.GetBadgeClass(),
            SubmittedAt = a.SubmittedAt.ToDateTimeUtc(),
            MotivationPreview = a.Motivation.Length > 100 ? a.Motivation[..100] + "..." : a.Motivation,
            MembershipTier = a.MembershipTier.ToString()
        }).ToList();

        var viewModel = new AdminApplicationListViewModel
        {
            Applications = applications,
            StatusFilter = status,
            TierFilter = tier,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpGet("Applications/{id}")]
    public async Task<IActionResult> ApplicationDetail(Guid id)
    {
        var application = await _applicationDecisionService.GetApplicationDetailAsync(id);

        if (application == null)
        {
            return NotFound();
        }

        var viewModel = new AdminApplicationDetailViewModel
        {
            Id = application.Id,
            UserId = application.UserId,
            UserEmail = application.User.Email ?? string.Empty,
            UserDisplayName = application.User.DisplayName,
            UserProfilePictureUrl = application.User.ProfilePictureUrl,
            Status = application.Status.ToString(),
            Motivation = application.Motivation,
            AdditionalInfo = application.AdditionalInfo,
            SignificantContribution = application.SignificantContribution,
            RoleUnderstanding = application.RoleUnderstanding,
            MembershipTier = application.MembershipTier,
            Language = application.Language,
            SubmittedAt = application.SubmittedAt.ToDateTimeUtc(),
            ReviewStartedAt = application.ReviewStartedAt?.ToDateTimeUtc(),
            ReviewerName = application.ReviewedByUser?.DisplayName,
            ReviewNotes = application.ReviewNotes,
            CanApproveReject = application.Status == ApplicationStatus.Submitted,
            History = application.StateHistory
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new ApplicationHistoryViewModel
                {
                    Status = h.Status.ToString(),
                    ChangedAt = h.ChangedAt.ToDateTimeUtc(),
                    ChangedBy = h.ChangedByUser.DisplayName,
                    Notes = h.Notes
                }).ToList()
        };

        return View(viewModel);
    }


    [HttpPost("Humans/{id}/Suspend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuspendHuman(Guid id, string? notes)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
            return NotFound();

        var result = await _onboardingService.SuspendAsync(id, currentUser.Id, currentUser.DisplayName, notes);
        if (!result.Success)
            return NotFound();

        TempData["SuccessMessage"] = _localizer["Admin_MemberSuspended"].Value;
        return RedirectToAction(nameof(HumanDetail), new { id });
    }

    [HttpPost("Humans/{id}/Unsuspend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnsuspendHuman(Guid id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
            return NotFound();

        var result = await _onboardingService.UnsuspendAsync(id, currentUser.Id, currentUser.DisplayName);
        if (!result.Success)
            return NotFound();

        TempData["SuccessMessage"] = _localizer["Admin_MemberUnsuspended"].Value;
        return RedirectToAction(nameof(HumanDetail), new { id });
    }

    [HttpPost("Humans/{id}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveVolunteer(Guid id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
            return NotFound();

        var result = await _onboardingService.ApproveVolunteerAsync(id, currentUser.Id, currentUser.DisplayName);
        if (!result.Success)
            return NotFound();

        TempData["SuccessMessage"] = _localizer["Admin_VolunteerApproved"].Value;
        return RedirectToAction(nameof(HumanDetail), new { id });
    }

    [HttpPost("Humans/{id}/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectSignup(Guid id, string? reason)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
            return Unauthorized();

        var result = await _onboardingService.RejectSignupAsync(id, currentUser.Id, currentUser.DisplayName, reason);
        if (!result.Success)
        {
            if (string.Equals(result.ErrorKey, "AlreadyRejected", StringComparison.Ordinal))
                TempData["ErrorMessage"] = "This human has already been rejected.";
            else
                return NotFound();
            return RedirectToAction(nameof(HumanDetail), new { id });
        }

        TempData["SuccessMessage"] = "Signup rejected.";
        return RedirectToAction(nameof(HumanDetail), new { id });
    }

    [HttpPost("Humans/{id}/Purge")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PurgeHuman(Guid id)
    {
        if (_environment.IsProduction())
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        if (user.Id == currentUser?.Id)
        {
            TempData["ErrorMessage"] = "You cannot purge your own account.";
            return RedirectToAction(nameof(HumanDetail), new { id });
        }

        var displayName = user.DisplayName;

        _logger.LogWarning(
            "Admin {AdminId} purging human {HumanId} ({DisplayName}) in {Environment}",
            currentUser?.Id, id, displayName, _environment.EnvironmentName);

        // Sever OAuth link so next Google login creates a fresh user
        var logins = await _userManager.GetLoginsAsync(user);
        foreach (var login in logins)
        {
            await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        }

        var result = await _onboardingService.PurgeHumanAsync(id);
        if (!result.Success)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = $"Purged {displayName}. They will get a fresh account on next login.";
        return RedirectToAction(nameof(Humans));
    }

    [HttpGet("Teams")]
    public async Task<IActionResult> Teams(int page = 1)
    {
        var pageSize = 20;
        var (teams, totalCount) = await _teamService.GetAllTeamsForAdminAsync(page, pageSize);

        var viewModel = new AdminTeamListViewModel
        {
            Teams = teams.Select(t => new AdminTeamViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                IsActive = t.IsActive,
                RequiresApproval = t.RequiresApproval,
                IsSystemTeam = t.IsSystemTeam,
                SystemTeamType = t.SystemTeamType != SystemTeamType.None ? t.SystemTeamType.ToString() : null,
                MemberCount = t.Members.Count,
                PendingRequestCount = t.JoinRequests.Count,
                CreatedAt = t.CreatedAt.ToDateTimeUtc()
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpGet("Teams/Create")]
    public IActionResult CreateTeam()
    {
        return View(new CreateTeamViewModel());
    }

    [HttpPost("Teams/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTeam(CreateTeamViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var team = await _teamService.CreateTeamAsync(model.Name, model.Description, model.RequiresApproval);
            var currentUser = await _userManager.GetUserAsync(User);
            _logger.LogInformation("Admin {AdminId} created team {TeamId} ({TeamName})", currentUser?.Id, team.Id, team.Name);

            TempData["SuccessMessage"] = string.Format(_localizer["Admin_TeamCreated"].Value, team.Name);
            return RedirectToAction(nameof(Teams));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating team");
            ModelState.AddModelError("", _localizer["Admin_TeamCreateError"].Value);
            return View(model);
        }
    }

    [HttpGet("Teams/{id}/Edit")]
    public async Task<IActionResult> EditTeam(Guid id)
    {
        var team = await _teamService.GetTeamByIdAsync(id);
        if (team == null)
        {
            return NotFound();
        }

        var viewModel = new EditTeamViewModel
        {
            Id = team.Id,
            Name = team.Name,
            Description = team.Description,
            RequiresApproval = team.RequiresApproval,
            IsActive = team.IsActive,
            IsSystemTeam = team.IsSystemTeam
        };

        return View(viewModel);
    }

    [HttpPost("Teams/{id}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTeam(Guid id, EditTeamViewModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _teamService.UpdateTeamAsync(id, model.Name, model.Description, model.RequiresApproval, model.IsActive);
            var currentUser = await _userManager.GetUserAsync(User);
            _logger.LogInformation("Admin {AdminId} updated team {TeamId}", currentUser?.Id, id);

            TempData["SuccessMessage"] = _localizer["Admin_TeamUpdated"].Value;
            return RedirectToAction(nameof(Teams));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(model);
        }
    }

    [HttpPost("Teams/{id}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTeam(Guid id)
    {
        try
        {
            await _teamService.DeleteTeamAsync(id);
            var currentUser = await _userManager.GetUserAsync(User);
            _logger.LogInformation("Admin {AdminId} deactivated team {TeamId}", currentUser?.Id, id);

            TempData["SuccessMessage"] = _localizer["Admin_TeamDeactivated"].Value;
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Teams));
    }

    [HttpGet("Roles")]
    public async Task<IActionResult> Roles(string? role, bool showInactive = false, int page = 1)
    {
        var pageSize = 50;
        var now = _clock.GetCurrentInstant();

        var (assignments, totalCount) = await _roleAssignmentService.GetFilteredAsync(
            role, activeOnly: !showInactive, page, pageSize, now);

        var viewModel = new AdminRoleAssignmentListViewModel
        {
            RoleAssignments = assignments.Select(ra => new AdminRoleAssignmentViewModel
            {
                Id = ra.Id,
                UserId = ra.UserId,
                UserEmail = ra.User.Email ?? string.Empty,
                UserDisplayName = ra.User.DisplayName,
                RoleName = ra.RoleName,
                ValidFrom = ra.ValidFrom.ToDateTimeUtc(),
                ValidTo = ra.ValidTo?.ToDateTimeUtc(),
                Notes = ra.Notes,
                IsActive = ra.IsActive(now),
                CreatedByName = ra.CreatedByUser?.DisplayName,
                CreatedAt = ra.CreatedAt.ToDateTimeUtc()
            }).ToList(),
            RoleFilter = role,
            ShowInactive = showInactive,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpGet("Humans/{id}/Roles/Add")]
    public async Task<IActionResult> AddRole(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var viewModel = new CreateRoleAssignmentViewModel
        {
            UserId = id,
            UserDisplayName = user.DisplayName,
            AvailableRoles = User.IsInRole(RoleNames.Admin)
                ? [RoleNames.Admin, RoleNames.Board, RoleNames.TeamsAdmin, RoleNames.ConsentCoordinator, RoleNames.VolunteerCoordinator]
                : [RoleNames.Board, RoleNames.TeamsAdmin, RoleNames.ConsentCoordinator, RoleNames.VolunteerCoordinator]
        };

        return View(viewModel);
    }

    [HttpPost("Humans/{id}/Roles/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRole(Guid id, CreateRoleAssignmentViewModel model)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(model.RoleName))
        {
            ModelState.AddModelError(nameof(model.RoleName), "Please select a role.");
            model.UserId = id;
            model.UserDisplayName = user.DisplayName;
            model.AvailableRoles = User.IsInRole(RoleNames.Admin)
                ? [RoleNames.Admin, RoleNames.Board, RoleNames.TeamsAdmin, RoleNames.ConsentCoordinator, RoleNames.VolunteerCoordinator]
                : [RoleNames.Board, RoleNames.TeamsAdmin, RoleNames.ConsentCoordinator, RoleNames.VolunteerCoordinator];
            return View(model);
        }

        // Enforce role assignment authorization
        if (!CanManageRole(model.RoleName))
        {
            return Forbid();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var result = await _roleAssignmentService.AssignRoleAsync(
            id, model.RoleName, currentUser.Id, currentUser.DisplayName, model.Notes);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Admin_RoleAlreadyActive"].Value, model.RoleName);
            return RedirectToAction(nameof(HumanDetail), new { id });
        }

        TempData["SuccessMessage"] = string.Format(_localizer["Admin_RoleAssigned"].Value, model.RoleName);
        return RedirectToAction(nameof(HumanDetail), new { id });
    }

    [HttpPost("Roles/{id}/End")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndRole(Guid id, string? notes)
    {
        var roleAssignment = await _roleAssignmentService.GetByIdAsync(id);

        if (roleAssignment == null)
        {
            return NotFound();
        }

        // Enforce role assignment authorization
        if (!CanManageRole(roleAssignment.RoleName))
        {
            return Forbid();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var result = await _roleAssignmentService.EndRoleAsync(
            id, currentUser.Id, currentUser.DisplayName, notes);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = _localizer["Admin_RoleNotActive"].Value;
            return RedirectToAction(nameof(HumanDetail), new { id = roleAssignment.UserId });
        }

        TempData["SuccessMessage"] = string.Format(_localizer["Admin_RoleEnded"].Value, roleAssignment.RoleName, roleAssignment.User.DisplayName);
        return RedirectToAction(nameof(HumanDetail), new { id = roleAssignment.UserId });
    }

    [HttpGet("GoogleSync")]
    public async Task<IActionResult> GoogleSync()
    {
        var drivePreview = await _googleSyncService.SyncResourcesByTypeAsync(
            GoogleResourceType.DriveFolder, SyncAction.Preview);
        var groupPreview = await _googleSyncService.SyncResourcesByTypeAsync(
            GoogleResourceType.Group, SyncAction.Preview);

        var allDiffs = drivePreview.Diffs.Concat(groupPreview.Diffs).ToList();

        var viewModel = new GoogleSyncViewModel
        {
            TotalResources = allDiffs.Count,
            InSyncCount = allDiffs.Count(d => d.IsInSync),
            DriftCount = allDiffs.Count(d => !d.IsInSync && d.ErrorMessage == null),
            ErrorCount = allDiffs.Count(d => d.ErrorMessage != null),
            Resources = allDiffs
                .OrderBy(d => d.IsInSync)
                .ThenBy(d => string.Join(", ", d.LinkedTeams), StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.ResourceName, StringComparer.OrdinalIgnoreCase)
                .Select(d => new GoogleSyncResourceViewModel
                {
                    ResourceId = d.ResourceId,
                    ResourceName = d.ResourceName,
                    ResourceType = d.ResourceType,
                    TeamName = string.Join(", ", d.LinkedTeams),
                    Url = d.Url,
                    ErrorMessage = d.ErrorMessage,
                    IsInSync = d.IsInSync,
                    MembersToAdd = d.MembersToAdd,
                    MembersToRemove = d.MembersToRemove
                }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost("SyncSystemTeams")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncSystemTeams(
        [FromServices] Infrastructure.Jobs.SystemTeamSyncJob systemTeamSyncJob)
    {
        try
        {
            await systemTeamSyncJob.ExecuteAsync();
            TempData["SuccessMessage"] = "System teams synced successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync system teams");
            TempData["ErrorMessage"] = $"Sync failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("GoogleSync/Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GoogleSyncApply()
    {
        var currentUser = await _userManager.GetUserAsync(User);

        try
        {
            await _googleSyncService.SyncResourcesByTypeAsync(GoogleResourceType.DriveFolder, SyncAction.AddOnly);
            await _googleSyncService.SyncResourcesByTypeAsync(GoogleResourceType.Group, SyncAction.AddOnly);
            _logger.LogInformation("Admin {AdminId} triggered manual Google resource sync", currentUser?.Id);
            TempData["SuccessMessage"] = _localizer["Admin_GoogleSyncSuccess"].Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual Google resource sync failed");
            TempData["ErrorMessage"] = _localizer["Admin_GoogleSyncFailed"].Value;
        }

        return RedirectToAction(nameof(GoogleSync));
    }

    [HttpGet("AuditLog")]
    public async Task<IActionResult> AuditLog(string? filter, int page = 1)
    {
        var pageSize = 50;
        var (items, totalCount, anomalyCount) = await _auditLogService.GetFilteredAsync(filter, page, pageSize);

        var entries = items.Select(e => new AuditLogEntryViewModel
        {
            Action = e.Action.ToString(),
            Description = e.Description,
            OccurredAt = e.OccurredAt.ToDateTimeUtc(),
            ActorName = e.ActorName,
            IsSystemAction = e.ActorUserId == null
        }).ToList();

        var viewModel = new AuditLogListViewModel
        {
            Entries = entries,
            ActionFilter = filter,
            AnomalyCount = anomalyCount,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpPost("AuditLog/CheckDriveActivity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckDriveActivity(
        [FromServices] IDriveActivityMonitorService monitorService)
    {
        var currentUser = await _userManager.GetUserAsync(User);

        try
        {
            var count = await monitorService.CheckForAnomalousActivityAsync();
            _logger.LogInformation("Admin {AdminId} triggered manual Drive activity check: {Count} anomalies",
                currentUser?.Id, count);

            TempData["SuccessMessage"] = count > 0
                ? $"Drive activity check completed: {count} anomalous change(s) detected."
                : "Drive activity check completed: no anomalies detected.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual Drive activity check failed");
            TempData["ErrorMessage"] = "Drive activity check failed. Check logs for details.";
        }

        return RedirectToAction(nameof(AuditLog), new { filter = nameof(AuditAction.AnomalousPermissionDetected) });
    }

    [HttpGet("GoogleSync/Resource/{id}/Audit")]
    public async Task<IActionResult> GoogleSyncResourceAudit(Guid id)
    {
        var resource = await _teamResourceService.GetResourceByIdAsync(id);

        if (resource == null)
        {
            return NotFound();
        }

        var entries = await _auditLogService.GetByResourceAsync(id);

        var viewModel = new GoogleSyncAuditListViewModel
        {
            Title = $"Sync Audit: {resource.Name}",
            BackUrl = Url.Action(nameof(GoogleSync)),
            BackLabel = "Back to Google Sync",
            Entries = entries.Select(e => new GoogleSyncAuditEntryViewModel
            {
                Action = e.Action.ToString(),
                Description = e.Description,
                UserEmail = e.UserEmail,
                Role = e.Role,
                SyncSource = e.SyncSource?.ToString(),
                OccurredAt = e.OccurredAt.ToDateTimeUtc(),
                Success = e.Success,
                ErrorMessage = e.ErrorMessage,
                ActorName = e.ActorName,
                RelatedEntityId = e.RelatedEntityId
            }).ToList()
        };

        return View("GoogleSyncAudit", viewModel);
    }

    [HttpGet("Humans/{id}/GoogleSyncAudit")]
    public async Task<IActionResult> HumanGoogleSyncAudit(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());

        if (user == null)
        {
            return NotFound();
        }

        var entries = await _auditLogService.GetGoogleSyncByUserAsync(id);

        var viewModel = new GoogleSyncAuditListViewModel
        {
            Title = $"Google Sync Audit: {user.DisplayName}",
            BackUrl = Url.Action(nameof(HumanDetail), new { id }),
            BackLabel = "Back to Member Detail",
            Entries = entries.Select(e => new GoogleSyncAuditEntryViewModel
            {
                Action = e.Action.ToString(),
                Description = e.Description,
                UserEmail = e.UserEmail,
                Role = e.Role,
                SyncSource = e.SyncSource?.ToString(),
                OccurredAt = e.OccurredAt.ToDateTimeUtc(),
                Success = e.Success,
                ErrorMessage = e.ErrorMessage,
                ActorName = e.ActorName,
                ResourceName = e.Resource?.Name,
                ResourceId = e.ResourceId
            }).ToList()
        };

        return View("GoogleSyncAudit", viewModel);
    }

    [HttpGet("Configuration")]
    public IActionResult Configuration([FromServices] IConfiguration configuration)
    {
        var keys = new (string Section, string Key, bool Required)[]
        {
            ("Authentication", "Authentication:Google:ClientId", true),
            ("Authentication", "Authentication:Google:ClientSecret", true),
            ("Database", "ConnectionStrings:DefaultConnection", true),
            ("Email", "Email:SmtpHost", true),
            ("Email", "Email:Username", true),
            ("Email", "Email:Password", true),
            ("Email", "Email:FromAddress", true),
            ("Email", "Email:BaseUrl", true),
            ("Google Workspace", "GoogleWorkspace:ServiceAccountKeyPath", false),
            ("Google Workspace", "GoogleWorkspace:ServiceAccountKeyJson", false),
            ("Google Workspace", "GoogleWorkspace:Domain", false),
            ("GitHub", "GitHub:Owner", true),
            ("GitHub", "GitHub:Repository", true),
            ("GitHub", "GitHub:AccessToken", true),
            ("Google Maps", "GoogleMaps:ApiKey", true),
            ("OpenTelemetry", "OpenTelemetry:OtlpEndpoint", false),
        };

        var items = keys.Select(k =>
        {
            var value = configuration[k.Key];
            var isSet = !string.IsNullOrEmpty(value);
            string preview = "(not set)";
            if (isSet)
            {
                preview = value![..Math.Min(3, value!.Length)] + "...";
            }

            return new ConfigurationItemViewModel
            {
                Section = k.Section,
                Key = k.Key,
                IsSet = isSet,
                Preview = preview,
                IsRequired = k.Required,
            };
        }).ToList();

        return View(new AdminConfigurationViewModel { Items = items });
    }
    [HttpGet("EmailPreview")]
    public IActionResult EmailPreview(
        [FromServices] IEmailRenderer renderer,
        [FromServices] IOptions<EmailSettings> emailSettings)
    {
        var settings = emailSettings.Value;
        var cultures = new[] { "en", "es", "de", "fr", "it" };

        // Per-locale persona stubs for realistic previews
        var personas = new Dictionary<string, (string Name, string Email)>(StringComparer.Ordinal)
        {
            ["en"] = ("Sally Smith", "sally@example.com"),
            ["es"] = ("Mar\u00eda Garc\u00eda", "maria@example.com"),
            ["de"] = ("Frieda Fischer", "frieda@example.com"),
            ["fr"] = ("Fran\u00e7ois Dupont", "francois@example.com"),
            ["it"] = ("Giulia Rossi", "giulia@example.com"),
        };

        var sampleDocs = new[] { "Volunteer Agreement", "Privacy Policy" };
        var sampleResources = new (string Name, string? Url)[]
        {
            ("Art Collective Shared Drive", "https://drive.google.com/drive/folders/example"),
            ("art-collective@nobodies.team", "https://groups.google.com/g/art-collective"),
        };

        var previews = new Dictionary<string, List<EmailPreviewItem>>(StringComparer.Ordinal);

        foreach (var culture in cultures)
        {
            var (name, email) = personas[culture];

            var items = new List<EmailPreviewItem>();

            var c1 = renderer.RenderApplicationSubmitted(Guid.Empty, name);
            items.Add(new EmailPreviewItem { Id = "application-submitted", Name = "Application Submitted (to Admin)", Recipient = settings.AdminAddress, Subject = c1.Subject, Body = c1.HtmlBody });

            var c2 = renderer.RenderApplicationApproved(name, MembershipTier.Colaborador, culture);
            items.Add(new EmailPreviewItem { Id = "application-approved", Name = "Application Approved", Recipient = email, Subject = c2.Subject, Body = c2.HtmlBody });

            var c3 = renderer.RenderApplicationRejected(name, MembershipTier.Asociado, "Incomplete profile information", culture);
            items.Add(new EmailPreviewItem { Id = "application-rejected", Name = "Application Rejected", Recipient = email, Subject = c3.Subject, Body = c3.HtmlBody });

            var c4 = renderer.RenderSignupRejected(name, "Incomplete profile information", culture);
            items.Add(new EmailPreviewItem { Id = "signup-rejected", Name = "Signup Rejected", Recipient = email, Subject = c4.Subject, Body = c4.HtmlBody });

            var c5 = renderer.RenderReConsentsRequired(name, new[] { sampleDocs[0] }, culture);
            items.Add(new EmailPreviewItem { Id = "reconsent-required", Name = "Re-Consent Required (single doc)", Recipient = email, Subject = c5.Subject, Body = c5.HtmlBody });

            var c6 = renderer.RenderReConsentsRequired(name, sampleDocs, culture);
            items.Add(new EmailPreviewItem { Id = "reconsents-required", Name = "Re-Consents Required (multiple docs)", Recipient = email, Subject = c6.Subject, Body = c6.HtmlBody });

            var c7 = renderer.RenderReConsentReminder(name, sampleDocs, 14, culture);
            items.Add(new EmailPreviewItem { Id = "reconsent-reminder", Name = "Re-Consent Reminder", Recipient = email, Subject = c7.Subject, Body = c7.HtmlBody });

            var c8 = renderer.RenderWelcome(name, culture);
            items.Add(new EmailPreviewItem { Id = "welcome", Name = "Welcome", Recipient = email, Subject = c8.Subject, Body = c8.HtmlBody });

            var c9 = renderer.RenderAccessSuspended(name, "Outstanding consent requirements", culture);
            items.Add(new EmailPreviewItem { Id = "access-suspended", Name = "Access Suspended", Recipient = email, Subject = c9.Subject, Body = c9.HtmlBody });

            var c10 = renderer.RenderEmailVerification(name, "preferred@example.com", $"{settings.BaseUrl}/Profile/VerifyEmail?token=sample-token", culture);
            items.Add(new EmailPreviewItem { Id = "email-verification", Name = "Email Verification", Recipient = "preferred@example.com", Subject = c10.Subject, Body = c10.HtmlBody });

            var c11 = renderer.RenderAccountDeletionRequested(name, "March 15, 2026", culture);
            items.Add(new EmailPreviewItem { Id = "deletion-requested", Name = "Account Deletion Requested", Recipient = email, Subject = c11.Subject, Body = c11.HtmlBody });

            var c12 = renderer.RenderAccountDeleted(name, culture);
            items.Add(new EmailPreviewItem { Id = "account-deleted", Name = "Account Deleted", Recipient = email, Subject = c12.Subject, Body = c12.HtmlBody });

            var c13 = renderer.RenderAddedToTeam(name, "Art Collective", "art-collective", sampleResources, culture);
            items.Add(new EmailPreviewItem { Id = "added-to-team", Name = "Added to Team", Recipient = email, Subject = c13.Subject, Body = c13.HtmlBody });

            var c14 = renderer.RenderTermRenewalReminder(name, "Colaborador", "April 1, 2026", culture);
            items.Add(new EmailPreviewItem { Id = "term-renewal-reminder", Name = "Term Renewal Reminder", Recipient = email, Subject = c14.Subject, Body = c14.HtmlBody });

            var sampleDigestGroups = new List<BoardDigestTierGroup>
            {
                new("Volunteer", new[] { "Alice Johnson", "Bob Smith" }),
                new("Colaborador", new[] { "Carlos García" })
            };
            var sampleOutstanding = new BoardDigestOutstandingCounts(
                OnboardingReview: 3,
                StillOnboarding: 5,
                BoardVotingTotal: 7,
                BoardVotingYours: 4,
                TeamJoinRequests: 2,
                PendingConsents: 12,
                PendingDeletions: 1);
            var c15 = renderer.RenderBoardDailyDigest(name, "2026-02-22", sampleDigestGroups, sampleOutstanding, culture);
            items.Add(new EmailPreviewItem { Id = "board-daily-digest", Name = "Board Daily Digest", Recipient = email, Subject = c15.Subject, Body = c15.HtmlBody });

            previews[culture] = items;
        }

        return View(new EmailPreviewViewModel { Previews = previews });
    }

    // Intentionally anonymous: exposes only migration names and counts (no sensitive data).
    // Used by dev tooling to check which migrations have been applied in QA/prod,
    // so old migrations can be safely squashed and removed from the repo.
    [HttpGet("DbVersion")]
    [AllowAnonymous]
    [Produces("application/json")]
    public async Task<IActionResult> DbVersion()
    {
        var applied = (await _dbContext.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = await _dbContext.Database.GetPendingMigrationsAsync();

        return Ok(new
        {
            lastApplied = applied.LastOrDefault(),
            appliedCount = applied.Count,
            pendingCount = pending.Count()
        });
    }

    /// <summary>
    /// Checks whether the current user can assign/end the specified role.
    /// Admin can manage any role. Board can manage Board and coordinator roles.
    /// </summary>
    private bool CanManageRole(string roleName)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return true;
        }

        // Board members can manage Board and coordinator roles
        if (User.IsInRole(RoleNames.Board))
        {
            return string.Equals(roleName, RoleNames.Board, StringComparison.Ordinal) ||
                   string.Equals(roleName, RoleNames.TeamsAdmin, StringComparison.Ordinal) ||
                   string.Equals(roleName, RoleNames.ConsentCoordinator, StringComparison.Ordinal) ||
                   string.Equals(roleName, RoleNames.VolunteerCoordinator, StringComparison.Ordinal);
        }

        return false;
    }
}
