using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using NodaTime;
using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Configuration;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Jobs;
using Humans.Web.Extensions;
using Humans.Web.Models;
using MemberApplication = Humans.Domain.Entities.Application;

namespace Humans.Web.Controllers;

[Authorize(Roles = "Board,Admin")]
[Route("Admin")]
public partial class AdminController : Controller
{
    private readonly HumansDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly ITeamService _teamService;
    private readonly IGoogleSyncService _googleSyncService;
    private readonly IAuditLogService _auditLogService;
    private readonly IMembershipCalculator _membershipCalculator;
    private readonly ILegalDocumentSyncService _legalDocumentSyncService;
    private readonly GitHubSettings _githubSettings;
    private readonly IClock _clock;
    private readonly ILogger<AdminController> _logger;
    private readonly SystemTeamSyncJob _systemTeamSyncJob;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AdminController(
        HumansDbContext dbContext,
        UserManager<User> userManager,
        ITeamService teamService,
        IGoogleSyncService googleSyncService,
        IAuditLogService auditLogService,
        IMembershipCalculator membershipCalculator,
        ILegalDocumentSyncService legalDocumentSyncService,
        IOptions<GitHubSettings> githubSettings,
        IClock clock,
        ILogger<AdminController> logger,
        SystemTeamSyncJob systemTeamSyncJob,
        IStringLocalizer<SharedResource> localizer)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _teamService = teamService;
        _googleSyncService = googleSyncService;
        _auditLogService = auditLogService;
        _membershipCalculator = membershipCalculator;
        _legalDocumentSyncService = legalDocumentSyncService;
        _githubSettings = githubSettings.Value;
        _clock = clock;
        _logger = logger;
        _systemTeamSyncJob = systemTeamSyncJob;
        _localizer = localizer;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var totalMembers = await _dbContext.Users.CountAsync();
        var pendingApplications = await _dbContext.Applications
            .CountAsync(a => a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.UnderReview);
        var pendingVolunteers = await _dbContext.Profiles
            .CountAsync(p => !p.IsApproved && !p.IsSuspended);

        // Calculate users with missing required consents
        var allUserIds = await _dbContext.Users.Select(u => u.Id).ToListAsync();
        var usersWithAllConsents = await _membershipCalculator.GetUsersWithAllRequiredConsentsAsync(allUserIds);
        var pendingConsents = allUserIds.Count - usersWithAllConsents.Count;

        var recentActivity = await _dbContext.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(15)
            .Select(e => new RecentActivityViewModel
            {
                Description = e.Description,
                Timestamp = e.OccurredAt.ToDateTimeUtc(),
                Type = e.Action.ToString()
            })
            .ToListAsync();

        var viewModel = new AdminDashboardViewModel
        {
            TotalMembers = totalMembers,
            ActiveMembers = await _dbContext.Profiles.CountAsync(p => !p.IsSuspended),
            PendingVolunteers = pendingVolunteers,
            PendingApplications = pendingApplications,
            PendingConsents = pendingConsents,
            RecentActivity = recentActivity
        };

        return View(viewModel);
    }

    [HttpGet("Members")]
    public async Task<IActionResult> Members(string? search, string? filter, int page = 1)
    {
        var pageSize = 20;
        var query = _dbContext.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Email!.Contains(search) ||
                u.DisplayName.Contains(search));
        }

        if (string.Equals(filter, "pending", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(u => u.Profile != null && !u.Profile.IsApproved && !u.Profile.IsSuspended);
        }

        var totalCount = await query.CountAsync();

        var members = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminMemberViewModel
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                DisplayName = u.DisplayName,
                ProfilePictureUrl = u.ProfilePictureUrl,
                CreatedAt = u.CreatedAt.ToDateTimeUtc(),
                LastLoginAt = u.LastLoginAt != null ? u.LastLoginAt.Value.ToDateTimeUtc() : null,
                HasProfile = u.Profile != null,
                IsApproved = u.Profile != null && u.Profile.IsApproved,
                MembershipStatus = u.Profile != null
                    ? (u.Profile.IsSuspended ? "Suspended" : (!u.Profile.IsApproved ? "Pending Approval" : "Active"))
                    : "Inactive"
            })
            .ToListAsync();

        var viewModel = new AdminMemberListViewModel
        {
            Members = members,
            SearchTerm = search,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpGet("Members/{id}")]
    public async Task<IActionResult> MemberDetail(Guid id)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profile)
            .Include(u => u.Applications)
            .Include(u => u.ConsentRecords)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        var now = _clock.GetCurrentInstant();
        var roleAssignments = await _dbContext.RoleAssignments
            .Include(ra => ra.CreatedByUser)
            .Where(ra => ra.UserId == id)
            .OrderByDescending(ra => ra.ValidFrom)
            .ToListAsync();

        // Query audit entries where the user is either the primary or related entity
        var auditEntries = await _dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(e =>
                (e.EntityType == "User" && e.EntityId == id) ||
                (e.RelatedEntityId == id))
            .OrderByDescending(e => e.OccurredAt)
            .Take(50)
            .Select(e => new AuditLogEntryViewModel
            {
                Action = e.Action.ToString(),
                Description = e.Description,
                OccurredAt = e.OccurredAt.ToDateTimeUtc(),
                ActorName = e.ActorName,
                IsSystemAction = e.ActorUserId == null
            })
            .ToListAsync();

        var viewModel = new AdminMemberDetailViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            CreatedAt = user.CreatedAt.ToDateTimeUtc(),
            LastLoginAt = user.LastLoginAt?.ToDateTimeUtc(),
            FirstName = user.Profile?.FirstName,
            LastName = user.Profile?.LastName,
            City = user.Profile?.City,
            CountryCode = user.Profile?.CountryCode,
            IsSuspended = user.Profile?.IsSuspended ?? false,
            IsApproved = user.Profile?.IsApproved ?? false,
            HasProfile = user.Profile != null,
            AdminNotes = user.Profile?.AdminNotes,
            ApplicationCount = user.Applications.Count,
            ConsentCount = user.ConsentRecords.Count,
            Applications = user.Applications
                .OrderByDescending(a => a.SubmittedAt)
                .Take(5)
                .Select(a => new AdminMemberApplicationViewModel
                {
                    Id = a.Id,
                    Status = a.Status.ToString(),
                    SubmittedAt = a.SubmittedAt.ToDateTimeUtc()
                }).ToList(),
            RoleAssignments = roleAssignments.Select(ra => new AdminRoleAssignmentViewModel
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
            AuditLog = auditEntries
        };

        return View(viewModel);
    }

    [HttpGet("Applications")]
    public async Task<IActionResult> Applications(string? status, int page = 1)
    {
        var pageSize = 20;
        var query = _dbContext.Applications
            .Include(a => a.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ApplicationStatus>(status, out var statusEnum))
        {
            query = query.Where(a => a.Status == statusEnum);
        }
        else
        {
            // Default: show pending applications
            query = query.Where(a =>
                a.Status == ApplicationStatus.Submitted ||
                a.Status == ApplicationStatus.UnderReview);
        }

        var totalCount = await query.CountAsync();

        var applications = await query
            .OrderBy(a => a.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminApplicationViewModel
            {
                Id = a.Id,
                UserId = a.UserId,
                UserEmail = a.User.Email ?? string.Empty,
                UserDisplayName = a.User.DisplayName,
                Status = a.Status.ToString(),
                StatusBadgeClass = a.Status.GetBadgeClass(),
                SubmittedAt = a.SubmittedAt.ToDateTimeUtc(),
                MotivationPreview = a.Motivation.Length > 100 ? a.Motivation.Substring(0, 100) + "..." : a.Motivation
            })
            .ToListAsync();

        var viewModel = new AdminApplicationListViewModel
        {
            Applications = applications,
            StatusFilter = status,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [HttpGet("Applications/{id}")]
    public async Task<IActionResult> ApplicationDetail(Guid id)
    {
        var application = await _dbContext.Applications
            .Include(a => a.User)
            .Include(a => a.ReviewedByUser)
            .Include(a => a.StateHistory)
                .ThenInclude(h => h.ChangedByUser)
            .FirstOrDefaultAsync(a => a.Id == id);

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
            Language = application.Language,
            SubmittedAt = application.SubmittedAt.ToDateTimeUtc(),
            ReviewStartedAt = application.ReviewStartedAt?.ToDateTimeUtc(),
            ReviewerName = application.ReviewedByUser?.DisplayName,
            ReviewNotes = application.ReviewNotes,
            CanStartReview = application.Status == ApplicationStatus.Submitted,
            CanApproveReject = application.Status == ApplicationStatus.UnderReview,
            History = application.StateHistory
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new ApplicationHistoryViewModel
                {
                    Status = h.Status.ToString(),
                    ChangedAt = h.ChangedAt.ToDateTimeUtc(),
                    ChangedBy = h.ChangedByUser?.DisplayName ?? "System",
                    Notes = h.Notes
                }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost("Applications/{id}/Action")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplicationAction(Guid id, AdminApplicationActionModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var application = await _dbContext.Applications
            .Include(a => a.StateHistory)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application == null)
        {
            return NotFound();
        }

        switch (model.Action.ToUpperInvariant())
        {
            case "STARTREVIEW":
                if (application.Status != ApplicationStatus.Submitted)
                {
                    TempData["ErrorMessage"] = _localizer["Admin_CannotStartReview"].Value;
                    break;
                }
                application.StartReview(currentUser.Id, _clock);
                _logger.LogInformation("Admin {AdminId} started review of application {ApplicationId}",
                    currentUser.Id, application.Id);
                TempData["SuccessMessage"] = _localizer["Admin_ReviewStarted"].Value;
                break;

            case "APPROVE":
                if (application.Status != ApplicationStatus.UnderReview)
                {
                    TempData["ErrorMessage"] = _localizer["Admin_CannotApprove"].Value;
                    break;
                }
                application.Approve(currentUser.Id, model.Notes, _clock);
                _logger.LogInformation("Admin {AdminId} approved application {ApplicationId}",
                    currentUser.Id, application.Id);
                TempData["SuccessMessage"] = _localizer["Admin_ApplicationApproved"].Value;
                break;

            case "REJECT":
                if (application.Status != ApplicationStatus.UnderReview)
                {
                    TempData["ErrorMessage"] = _localizer["Admin_CannotReject"].Value;
                    break;
                }
                if (string.IsNullOrWhiteSpace(model.Notes))
                {
                    TempData["ErrorMessage"] = _localizer["Admin_ProvideRejectionReason"].Value;
                    break;
                }
                application.Reject(currentUser.Id, model.Notes, _clock);
                _logger.LogInformation("Admin {AdminId} rejected application {ApplicationId}",
                    currentUser.Id, application.Id);
                TempData["SuccessMessage"] = _localizer["Admin_ApplicationRejected"].Value;
                break;

            case "REQUESTINFO":
                if (application.Status != ApplicationStatus.UnderReview)
                {
                    TempData["ErrorMessage"] = _localizer["Admin_CannotRequestInfo"].Value;
                    break;
                }
                if (string.IsNullOrWhiteSpace(model.Notes))
                {
                    TempData["ErrorMessage"] = _localizer["Admin_SpecifyInfoNeeded"].Value;
                    break;
                }
                application.RequestMoreInfo(currentUser.Id, model.Notes, _clock);
                _logger.LogInformation("Admin {AdminId} requested more info for application {ApplicationId}",
                    currentUser.Id, application.Id);
                TempData["SuccessMessage"] = _localizer["Admin_MoreInfoRequested"].Value;
                break;

            default:
                TempData["ErrorMessage"] = _localizer["Admin_UnknownAction"].Value;
                break;
        }

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(ApplicationDetail), new { id });
    }

    [HttpPost("Members/{id}/Suspend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuspendMember(Guid id, string? notes)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user?.Profile == null)
        {
            return NotFound();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        user.Profile.IsSuspended = true;
        user.Profile.AdminNotes = notes;
        user.Profile.UpdatedAt = _clock.GetCurrentInstant();

        if (currentUser != null)
        {
            await _auditLogService.LogAsync(
                AuditAction.MemberSuspended, "User", id,
                $"{user.DisplayName} suspended by {currentUser.DisplayName}{(string.IsNullOrWhiteSpace(notes) ? "" : $": {notes}")}",
                currentUser.Id, currentUser.DisplayName);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} suspended member {MemberId}", currentUser?.Id, id);

        TempData["SuccessMessage"] = _localizer["Admin_MemberSuspended"].Value;
        return RedirectToAction(nameof(MemberDetail), new { id });
    }

    [HttpPost("Members/{id}/Unsuspend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnsuspendMember(Guid id)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user?.Profile == null)
        {
            return NotFound();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        user.Profile.IsSuspended = false;
        user.Profile.UpdatedAt = _clock.GetCurrentInstant();

        if (currentUser != null)
        {
            await _auditLogService.LogAsync(
                AuditAction.MemberUnsuspended, "User", id,
                $"{user.DisplayName} unsuspended by {currentUser.DisplayName}",
                currentUser.Id, currentUser.DisplayName);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} unsuspended member {MemberId}", currentUser?.Id, id);

        TempData["SuccessMessage"] = _localizer["Admin_MemberUnsuspended"].Value;
        return RedirectToAction(nameof(MemberDetail), new { id });
    }

    [HttpPost("Members/{id}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveVolunteer(Guid id)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user?.Profile == null)
        {
            return NotFound();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var now = _clock.GetCurrentInstant();

        user.Profile.IsApproved = true;
        user.Profile.UpdatedAt = now;

        if (currentUser != null)
        {
            await _auditLogService.LogAsync(
                AuditAction.VolunteerApproved, "User", id,
                $"{user.DisplayName} approved as volunteer by {currentUser.DisplayName}",
                currentUser.Id, currentUser.DisplayName);
        }

        await _dbContext.SaveChangesAsync();

        // Sync Volunteers team membership (adds user if they also have all required consents)
        await _systemTeamSyncJob.SyncVolunteersMembershipForUserAsync(id);

        _logger.LogInformation("Admin {AdminId} approved volunteer {MemberId}", currentUser?.Id, id);

        TempData["SuccessMessage"] = _localizer["Admin_VolunteerApproved"].Value;
        return RedirectToAction(nameof(MemberDetail), new { id });
    }

    [HttpGet("Teams")]
    public async Task<IActionResult> Teams(int page = 1)
    {
        var pageSize = 20;
        var query = _dbContext.Teams
            .Include(t => t.Members.Where(m => m.LeftAt == null))
            .Include(t => t.JoinRequests.Where(r => r.Status == TeamJoinRequestStatus.Pending))
            .OrderBy(t => t.SystemTeamType)
            .ThenBy(t => t.Name);

        var totalCount = await query.CountAsync();

        var teams = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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
        var team = await _dbContext.Teams.FindAsync(id);
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

        var query = _dbContext.RoleAssignments
            .Include(ra => ra.User)
            .Include(ra => ra.CreatedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(ra => ra.RoleName == role);
        }

        if (!showInactive)
        {
            query = query.Where(ra => ra.ValidFrom <= now && (ra.ValidTo == null || ra.ValidTo > now));
        }

        var totalCount = await query.CountAsync();

        var assignments = await query
            .OrderBy(ra => ra.RoleName)
            .ThenByDescending(ra => ra.ValidFrom)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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

    [HttpGet("Members/{id}/Roles/Add")]
    public async Task<IActionResult> AddRole(Guid id)
    {
        var user = await _dbContext.Users.FindAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var viewModel = new CreateRoleAssignmentViewModel
        {
            UserId = id,
            UserDisplayName = user.DisplayName,
            AvailableRoles = User.IsInRole(RoleNames.Admin)
                ? [RoleNames.Admin, RoleNames.Board, RoleNames.Lead]
                : [RoleNames.Board, RoleNames.Lead]
        };

        return View(viewModel);
    }

    [HttpPost("Members/{id}/Roles/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRole(Guid id, CreateRoleAssignmentViewModel model)
    {
        var user = await _dbContext.Users.FindAsync(id);
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
                ? [RoleNames.Admin, RoleNames.Board, RoleNames.Lead]
                : [RoleNames.Board, RoleNames.Lead];
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

        var now = _clock.GetCurrentInstant();

        // Check if user already has an active assignment for this role
        var existingActive = await _dbContext.RoleAssignments
            .AnyAsync(ra => ra.UserId == id
                && ra.RoleName == model.RoleName
                && ra.ValidFrom <= now
                && (ra.ValidTo == null || ra.ValidTo > now));

        if (existingActive)
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Admin_RoleAlreadyActive"].Value, model.RoleName);
            return RedirectToAction(nameof(MemberDetail), new { id });
        }

        var roleAssignment = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = id,
            RoleName = model.RoleName,
            ValidFrom = now,
            Notes = model.Notes,
            CreatedAt = now,
            CreatedByUserId = currentUser.Id
        };

        _dbContext.RoleAssignments.Add(roleAssignment);

        await _auditLogService.LogAsync(
            AuditAction.RoleAssigned, "User", id,
            $"Role '{model.RoleName}' assigned to {user.DisplayName}",
            currentUser.Id, currentUser.DisplayName);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} assigned role {Role} to user {UserId}",
            currentUser.Id, model.RoleName, id);

        // Trigger sync for Board role changes
        if (string.Equals(model.RoleName, RoleNames.Board, StringComparison.Ordinal))
        {
            await _systemTeamSyncJob.SyncBoardTeamAsync();
        }

        TempData["SuccessMessage"] = string.Format(_localizer["Admin_RoleAssigned"].Value, model.RoleName);
        return RedirectToAction(nameof(MemberDetail), new { id });
    }

    [HttpPost("Roles/{id}/End")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndRole(Guid id, string? notes)
    {
        var roleAssignment = await _dbContext.RoleAssignments
            .Include(ra => ra.User)
            .FirstOrDefaultAsync(ra => ra.Id == id);

        if (roleAssignment == null)
        {
            return NotFound();
        }

        // Enforce role assignment authorization
        if (!CanManageRole(roleAssignment.RoleName))
        {
            return Forbid();
        }

        var now = _clock.GetCurrentInstant();

        if (!roleAssignment.IsActive(now))
        {
            TempData["ErrorMessage"] = _localizer["Admin_RoleNotActive"].Value;
            return RedirectToAction(nameof(MemberDetail), new { id = roleAssignment.UserId });
        }

        var currentUser = await _userManager.GetUserAsync(User);

        roleAssignment.ValidTo = now;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            roleAssignment.Notes = string.IsNullOrEmpty(roleAssignment.Notes)
                ? $"Ended: {notes}"
                : $"{roleAssignment.Notes} | Ended: {notes}";
        }

        if (currentUser != null)
        {
            await _auditLogService.LogAsync(
                AuditAction.RoleEnded, "User", roleAssignment.UserId,
                $"Role '{roleAssignment.RoleName}' ended for {roleAssignment.User.DisplayName}",
                currentUser.Id, currentUser.DisplayName);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} ended role {Role} for user {UserId}",
            currentUser?.Id, roleAssignment.RoleName, roleAssignment.UserId);

        // Trigger sync for Board role changes
        if (string.Equals(roleAssignment.RoleName, RoleNames.Board, StringComparison.Ordinal))
        {
            await _systemTeamSyncJob.SyncBoardTeamAsync();
        }

        TempData["SuccessMessage"] = string.Format(_localizer["Admin_RoleEnded"].Value, roleAssignment.RoleName, roleAssignment.User.DisplayName);
        return RedirectToAction(nameof(MemberDetail), new { id = roleAssignment.UserId });
    }

    [HttpGet("GoogleSync")]
    public async Task<IActionResult> GoogleSync()
    {
        var preview = await _googleSyncService.PreviewSyncAllAsync();

        var viewModel = new GoogleSyncViewModel
        {
            TotalResources = preview.TotalResources,
            InSyncCount = preview.InSyncCount,
            DriftCount = preview.DriftCount,
            ErrorCount = preview.Diffs.Count(d => d.ErrorMessage != null),
            Resources = preview.Diffs
                .OrderBy(d => d.IsInSync)
                .ThenBy(d => d.TeamName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.ResourceName, StringComparer.OrdinalIgnoreCase)
                .Select(d => new GoogleSyncResourceViewModel
                {
                    ResourceId = d.ResourceId,
                    ResourceName = d.ResourceName,
                    ResourceType = d.ResourceType,
                    TeamName = d.TeamName,
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
    public async Task<IActionResult> SyncSystemTeams()
    {
        try
        {
            await _systemTeamSyncJob.ExecuteAsync();
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
            await _googleSyncService.SyncAllResourcesAsync();
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
        var query = _dbContext.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter) && Enum.TryParse<AuditAction>(filter, out var actionEnum))
        {
            query = query.Where(e => e.Action == actionEnum);
        }

        var totalCount = await query.CountAsync();

        var entries = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditLogEntryViewModel
            {
                Action = e.Action.ToString(),
                Description = e.Description,
                OccurredAt = e.OccurredAt.ToDateTimeUtc(),
                ActorName = e.ActorName,
                IsSystemAction = e.ActorUserId == null
            })
            .ToListAsync();

        var anomalyCount = await _dbContext.AuditLogEntries
            .AsNoTracking()
            .CountAsync(e => e.Action == AuditAction.AnomalousPermissionDetected);

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

        return RedirectToAction(nameof(AuditLog), new { action = nameof(AuditAction.AnomalousPermissionDetected) });
    }

    [HttpGet("GoogleSync/Resource/{id}/Audit")]
    public async Task<IActionResult> GoogleSyncResourceAudit(Guid id)
    {
        var resource = await _dbContext.GoogleResources
            .AsNoTracking()
            .Include(r => r.Team)
            .FirstOrDefaultAsync(r => r.Id == id);

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

    [HttpGet("Members/{id}/GoogleSyncAudit")]
    public async Task<IActionResult> MemberGoogleSyncAudit(Guid id)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        var entries = await _auditLogService.GetGoogleSyncByUserAsync(id);

        var viewModel = new GoogleSyncAuditListViewModel
        {
            Title = $"Google Sync Audit: {user.DisplayName}",
            BackUrl = Url.Action(nameof(MemberDetail), new { id }),
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

    [HttpGet("LegalDocuments")]
    public async Task<IActionResult> LegalDocuments(Guid? teamId)
    {
        var query = _dbContext.LegalDocuments
            .Include(d => d.Team)
            .Include(d => d.Versions)
            .AsQueryable();

        if (teamId.HasValue)
        {
            query = query.Where(d => d.TeamId == teamId.Value);
        }

        var documents = await query
            .OrderBy(d => d.Team.Name)
            .ThenBy(d => d.Name)
            .ToListAsync();

        var now = _clock.GetCurrentInstant();

        var viewModel = new LegalDocumentListViewModel
        {
            FilterTeamId = teamId,
            Teams = await GetTeamSelectItems(),
            Documents = documents.Select(d =>
            {
                var currentVersion = d.Versions
                    .Where(v => v.EffectiveFrom <= now)
                    .MaxBy(v => v.EffectiveFrom);

                return new LegalDocumentListItemViewModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    TeamName = d.Team.Name,
                    TeamId = d.TeamId,
                    IsRequired = d.IsRequired,
                    IsActive = d.IsActive,
                    GracePeriodDays = d.GracePeriodDays,
                    CurrentVersion = currentVersion?.VersionNumber,
                    LastSyncedAt = d.LastSyncedAt != default ? d.LastSyncedAt.ToDateTimeUtc() : null,
                    VersionCount = d.Versions.Count
                };
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet("LegalDocuments/Create")]
    public async Task<IActionResult> CreateLegalDocument(Guid? teamId)
    {
        var viewModel = new LegalDocumentEditViewModel
        {
            TeamId = teamId ?? Guid.Empty,
            Teams = await GetTeamSelectItems()
        };

        return View(viewModel);
    }

    [HttpPost("LegalDocuments/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLegalDocument(LegalDocumentEditViewModel model)
    {
        var folderPath = NormalizeGitHubFolderPath(model.GitHubFolderPath);

        if (!ModelState.IsValid)
        {
            model.Teams = await GetTeamSelectItems();
            return View(model);
        }

        var now = _clock.GetCurrentInstant();

        var document = new LegalDocument
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            TeamId = model.TeamId,
            IsRequired = model.IsRequired,
            IsActive = model.IsActive,
            GracePeriodDays = model.GracePeriodDays,
            GitHubFolderPath = folderPath,
            CurrentCommitSha = string.Empty,
            CreatedAt = now
        };

        _dbContext.LegalDocuments.Add(document);
        await _dbContext.SaveChangesAsync();

        var currentUser = await _userManager.GetUserAsync(User);
        _logger.LogInformation("Admin {AdminId} created legal document {DocumentId} ({Name})",
            currentUser?.Id, document.Id, document.Name);

        // Attempt initial sync immediately
        if (!string.IsNullOrEmpty(document.GitHubFolderPath))
        {
            try
            {
                var result = await _legalDocumentSyncService.SyncDocumentAsync(document.Id);
                TempData["SuccessMessage"] = result != null
                    ? $"Legal document '{document.Name}' created. {result}"
                    : $"Legal document '{document.Name}' created. GitHub content is already up to date.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial sync failed for new document {DocumentId}", document.Id);
                TempData["SuccessMessage"] = $"Legal document '{document.Name}' created.";
                TempData["ErrorMessage"] = $"Initial sync failed: {ex.Message}";
            }
        }
        else
        {
            TempData["SuccessMessage"] = $"Legal document '{document.Name}' created. Set a GitHub Folder Path and sync to add content.";
        }

        return RedirectToAction(nameof(LegalDocuments));
    }

    [HttpGet("LegalDocuments/{id}/Edit")]
    public async Task<IActionResult> EditLegalDocument(Guid id)
    {
        var document = await _dbContext.LegalDocuments
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound();
        }

        var now = _clock.GetCurrentInstant();
        var currentVersion = document.Versions
            .Where(v => v.EffectiveFrom <= now)
            .MaxBy(v => v.EffectiveFrom);

        var viewModel = new LegalDocumentEditViewModel
        {
            Id = document.Id,
            Name = document.Name,
            TeamId = document.TeamId,
            IsRequired = document.IsRequired,
            IsActive = document.IsActive,
            GracePeriodDays = document.GracePeriodDays,
            GitHubFolderPath = document.GitHubFolderPath,
            Teams = await GetTeamSelectItems(),
            CurrentVersion = currentVersion?.VersionNumber,
            LastSyncedAt = document.LastSyncedAt != default ? document.LastSyncedAt.ToDateTimeUtc() : null,
            VersionCount = document.Versions.Count,
            Versions = document.Versions
                .OrderByDescending(v => v.EffectiveFrom)
                .Select(v => new DocumentVersionSummaryViewModel
                {
                    Id = v.Id,
                    VersionNumber = v.VersionNumber,
                    CommitSha = v.CommitSha,
                    EffectiveFrom = v.EffectiveFrom.ToDateTimeUtc(),
                    CreatedAt = v.CreatedAt.ToDateTimeUtc(),
                    ChangesSummary = v.ChangesSummary,
                    RequiresReConsent = v.RequiresReConsent,
                    LanguageCount = v.Content.Count,
                    Languages = v.Content.Keys.Order(StringComparer.Ordinal).ToList()
                })
                .ToList()
        };

        return View(viewModel);
    }

    [HttpPost("LegalDocuments/{id}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditLegalDocument(Guid id, LegalDocumentEditViewModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var folderPath = NormalizeGitHubFolderPath(model.GitHubFolderPath);

        if (!ModelState.IsValid)
        {
            model.Teams = await GetTeamSelectItems();
            return View(model);
        }

        var document = await _dbContext.LegalDocuments.FindAsync(id);
        if (document == null)
        {
            return NotFound();
        }

        document.Name = model.Name;
        document.TeamId = model.TeamId;
        document.IsRequired = model.IsRequired;
        document.IsActive = model.IsActive;
        document.GracePeriodDays = model.GracePeriodDays;
        document.GitHubFolderPath = folderPath;

        await _dbContext.SaveChangesAsync();

        var currentUser = await _userManager.GetUserAsync(User);
        _logger.LogInformation("Admin {AdminId} updated legal document {DocumentId}", currentUser?.Id, id);

        TempData["SuccessMessage"] = $"Legal document '{document.Name}' updated successfully.";
        return RedirectToAction(nameof(LegalDocuments));
    }

    [HttpPost("LegalDocuments/{id}/Archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ArchiveLegalDocument(Guid id)
    {
        var document = await _dbContext.LegalDocuments.FindAsync(id);
        if (document == null)
        {
            return NotFound();
        }

        document.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var currentUser = await _userManager.GetUserAsync(User);
        _logger.LogInformation("Admin {AdminId} archived legal document {DocumentId}", currentUser?.Id, id);

        TempData["SuccessMessage"] = $"Legal document '{document.Name}' archived.";
        return RedirectToAction(nameof(LegalDocuments));
    }

    [HttpPost("LegalDocuments/{id}/Sync")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncLegalDocument(Guid id)
    {
        try
        {
            var result = await _legalDocumentSyncService.SyncDocumentAsync(id);
            var currentUser = await _userManager.GetUserAsync(User);
            _logger.LogInformation("Admin {AdminId} triggered sync for legal document {DocumentId}", currentUser?.Id, id);

            TempData["SuccessMessage"] = result ?? "Document is already up to date.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing legal document {DocumentId}", id);
            TempData["ErrorMessage"] = $"Sync failed: {ex.Message}";
        }

        return RedirectToAction(nameof(EditLegalDocument), new { id });
    }

    [HttpPost("LegalDocuments/{id}/Versions/{versionId}/Summary")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVersionSummary(Guid id, Guid versionId, [FromForm] string changesSummary)
    {
        var version = await _dbContext.Set<DocumentVersion>()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.LegalDocumentId == id);

        if (version == null)
        {
            return NotFound();
        }

        version.ChangesSummary = string.IsNullOrWhiteSpace(changesSummary) ? null : changesSummary.Trim();
        await _dbContext.SaveChangesAsync();

        TempData["SuccessMessage"] = "Version summary updated.";
        return RedirectToAction(nameof(EditLegalDocument), new { id });
    }

    private async Task<List<TeamSelectItem>> GetTeamSelectItems()
    {
        return await _dbContext.Teams
            .Where(t => t.IsActive)
            .OrderBy(t => t.SystemTeamType)
            .ThenBy(t => t.Name)
            .Select(t => new TeamSelectItem { Id = t.Id, Name = t.Name })
            .ToListAsync();
    }

    /// <summary>
    /// Normalizes a GitHub folder path input. Accepts either a plain folder path
    /// (e.g. "Volunteer/") or a full GitHub URL (e.g.
    /// "https://github.com/owner/repo/tree/branch/Volunteer").
    /// Returns the extracted folder path, or null with a ModelState error if the URL
    /// doesn't match the configured repository.
    /// </summary>
    private string? NormalizeGitHubFolderPath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        input = input.Trim();

        // Match GitHub URLs: https://github.com/{owner}/{repo}/tree/{branch}/{path}
        var match = GitHubUrlPattern().Match(input);

        if (!match.Success)
        {
            // Not a URL — treat as a plain folder path
            return input.TrimEnd('/') + "/";
        }

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var branch = match.Groups["branch"].Value;
        var path = match.Groups["path"].Value;

        if (!string.Equals(owner, _githubSettings.Owner, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(repo, _githubSettings.Repository, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(LegalDocumentEditViewModel.GitHubFolderPath),
                $"URL points to {owner}/{repo}, but the configured repository is {_githubSettings.Owner}/{_githubSettings.Repository}.");
            return null;
        }

        if (!string.Equals(branch, _githubSettings.Branch, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(LegalDocumentEditViewModel.GitHubFolderPath),
                $"URL points to branch '{branch}', but the configured branch is '{_githubSettings.Branch}'.");
            return null;
        }

        return path.TrimEnd('/') + "/";
    }

    [GeneratedRegex(@"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/tree/(?<branch>[^/]+)/(?<path>[^\s]+)$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex GitHubUrlPattern();

    /// <summary>
    /// Checks whether the current user can assign/end the specified role.
    /// Admin can manage any role. Board can manage Board and Lead only.
    /// </summary>
    private bool CanManageRole(string roleName)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return true;
        }

        // Board members can manage Board and Lead roles
        if (User.IsInRole(RoleNames.Board))
        {
            return string.Equals(roleName, RoleNames.Board, StringComparison.Ordinal)
                || string.Equals(roleName, RoleNames.Lead, StringComparison.Ordinal);
        }

        return false;
    }
}
