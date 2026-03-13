using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Web.Models;
using NodaTime;

namespace Humans.Web.Controllers;

[Route("Barrios")]
public class BarrioController : Controller
{
    private readonly IBarrioService _barrioService;
    private readonly UserManager<User> _userManager;
    private readonly IClock _clock;
    private readonly ILogger<BarrioController> _logger;

    public BarrioController(
        IBarrioService barrioService,
        UserManager<User> userManager,
        IClock clock,
        ILogger<BarrioController> logger)
    {
        _barrioService = barrioService;
        _userManager = userManager;
        _clock = clock;
        _logger = logger;
    }

    // ======================================================================
    // Public routes
    // ======================================================================

    [AllowAnonymous]
    [HttpGet("")]
    public async Task<IActionResult> Index(BarrioFilterViewModel? filters)
    {
        var settings = await _barrioService.GetSettingsAsync();
        var year = settings.PublicYear;
        var barrios = await _barrioService.GetBarriosForYearAsync(year);

        var cards = barrios.Select(b =>
        {
            var season = b.Seasons.FirstOrDefault(s => s.Year == year);
            var firstImage = b.Images.OrderBy(i => i.SortOrder).FirstOrDefault();
            return new BarrioCardViewModel
            {
                Id = b.Id,
                Slug = b.Slug,
                Name = season?.Name ?? b.Slug,
                BlurbShort = season?.BlurbShort ?? string.Empty,
                ImageUrl = firstImage != null ? $"/{firstImage.StoragePath}" : null,
                Vibes = season?.Vibes ?? new List<BarrioVibe>(),
                AcceptingMembers = season?.AcceptingMembers ?? YesNoMaybe.No,
                KidsWelcome = season?.KidsWelcome ?? YesNoMaybe.No,
                SoundZone = season?.SoundZone,
                Status = season?.Status ?? BarrioSeasonStatus.Pending,
                TimesAtNowhere = b.TimesAtNowhere
            };
        }).ToList();

        // Apply filters
        if (filters?.Vibe.HasValue == true)
            cards = cards.Where(c => c.Vibes.Contains(filters.Vibe.Value)).ToList();
        if (filters?.SoundZone.HasValue == true)
            cards = cards.Where(c => c.SoundZone == filters.SoundZone.Value).ToList();
        if (filters?.KidsFriendly == true)
            cards = cards.Where(c => c.KidsWelcome == YesNoMaybe.Yes).ToList();
        if (filters?.AcceptingMembers == true)
            cards = cards.Where(c => c.AcceptingMembers == YesNoMaybe.Yes).ToList();

        var viewModel = new BarrioIndexViewModel
        {
            Year = year,
            Barrios = cards.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Filters = filters ?? new BarrioFilterViewModel()
        };

        return View(viewModel);
    }

    [AllowAnonymous]
    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null)
            return NotFound();

        var settings = await _barrioService.GetSettingsAsync();
        var currentSeason = barrio.Seasons
            .Where(s => s.Year == settings.PublicYear)
            .OrderByDescending(s => s.Year)
            .FirstOrDefault()
            ?? barrio.Seasons.OrderByDescending(s => s.Year).FirstOrDefault();

        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var isLead = false;
        var isPrimaryLead = false;
        var isCampAdmin = false;

        if (isAuthenticated)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
                isPrimaryLead = await _barrioService.IsUserPrimaryLeadAsync(user.Id, barrio.Id);
                isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
            }
        }

        var viewModel = new BarrioDetailViewModel
        {
            Id = barrio.Id,
            Slug = barrio.Slug,
            Name = currentSeason?.Name ?? barrio.Slug,
            ContactEmail = barrio.ContactEmail,
            ContactMethod = barrio.ContactMethod,
            WebOrSocialUrl = barrio.WebOrSocialUrl,
            IsSwissCamp = barrio.IsSwissCamp,
            TimesAtNowhere = barrio.TimesAtNowhere,
            HistoricalNames = barrio.HistoricalNames.Select(h => h.Name).ToList(),
            ImageUrls = barrio.Images.OrderBy(i => i.SortOrder).Select(i => $"/{i.StoragePath}").ToList(),
            Leads = barrio.Leads
                .Where(l => l.IsActive)
                .Select(l => new BarrioLeadViewModel
                {
                    UserId = l.UserId,
                    DisplayName = l.User.DisplayName,
                    Role = l.Role
                }).ToList(),
            CurrentSeason = currentSeason != null ? MapSeasonDetail(currentSeason) : null,
            IsCurrentUserLead = isLead,
            IsCurrentUserPrimaryLead = isPrimaryLead,
            IsCurrentUserCampAdmin = isCampAdmin
        };

        return View(viewModel);
    }

    // ======================================================================
    // Registration
    // ======================================================================

    [Authorize]
    [HttpGet("Register")]
    public async Task<IActionResult> Register()
    {
        var settings = await _barrioService.GetSettingsAsync();
        if (settings.OpenSeasons.Count == 0)
        {
            TempData["ErrorMessage"] = "Registration is currently closed.";
            return RedirectToAction(nameof(Index));
        }

        return View(new BarrioRegisterViewModel());
    }

    [Authorize]
    [HttpPost("Register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(BarrioRegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var settings = await _barrioService.GetSettingsAsync();
        var year = settings.OpenSeasons.OrderByDescending(y => y).FirstOrDefault();
        if (year == 0)
        {
            TempData["ErrorMessage"] = "Registration is currently closed.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var historicalNames = string.IsNullOrWhiteSpace(model.HistoricalNames)
                ? null
                : model.HistoricalNames
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

            var barrio = await _barrioService.CreateBarrioAsync(
                user.Id,
                model.Name,
                model.ContactEmail,
                model.ContactPhone,
                model.WebOrSocialUrl,
                model.ContactMethod,
                model.IsSwissCamp,
                model.TimesAtNowhere,
                MapToSeasonData(model),
                historicalNames,
                year);

            TempData["SuccessMessage"] = "Your barrio has been registered and is pending review.";
            return RedirectToAction(nameof(Details), new { slug = barrio.Slug });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    // ======================================================================
    // Edit
    // ======================================================================

    [Authorize]
    [HttpGet("{slug}/Edit")]
    public async Task<IActionResult> Edit(string slug)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        var settings = await _barrioService.GetSettingsAsync();
        var season = barrio.Seasons
            .Where(s => s.Year == settings.PublicYear)
            .OrderByDescending(s => s.Year)
            .FirstOrDefault()
            ?? barrio.Seasons.OrderByDescending(s => s.Year).FirstOrDefault();

        if (season is null)
        {
            TempData["ErrorMessage"] = "No season found for this barrio.";
            return RedirectToAction(nameof(Details), new { slug });
        }

        var viewModel = MapToEditViewModel(barrio, season);
        return View(viewModel);
    }

    [Authorize]
    [HttpPost("{slug}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string slug, BarrioEditViewModel model)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        if (!ModelState.IsValid)
        {
            // Re-populate read-only fields
            var season = barrio.Seasons.FirstOrDefault(s => s.Id == model.SeasonId);
            if (season is not null)
            {
                model.Leads = barrio.Leads.Where(l => l.IsActive)
                    .Select(l => new BarrioLeadViewModel
                    {
                        UserId = l.UserId,
                        DisplayName = l.User.DisplayName,
                        Role = l.Role
                    }).ToList();
                model.Images = barrio.Images.OrderBy(i => i.SortOrder)
                    .Select(i => new BarrioImageViewModel
                    {
                        Id = i.Id,
                        Url = $"/{i.StoragePath}",
                        SortOrder = i.SortOrder
                    }).ToList();
            }
            return View(model);
        }

        try
        {
            await _barrioService.UpdateBarrioAsync(
                barrio.Id,
                model.ContactEmail,
                model.ContactPhone,
                model.WebOrSocialUrl,
                model.ContactMethod,
                model.IsSwissCamp,
                model.TimesAtNowhere);

            await _barrioService.UpdateSeasonAsync(model.SeasonId, MapToSeasonData(model));

            // Handle name change if not locked
            var season = barrio.Seasons.FirstOrDefault(s => s.Id == model.SeasonId);
            if (season is not null && !string.Equals(season.Name, model.Name, StringComparison.Ordinal))
            {
                var today = _clock.GetCurrentInstant().InUtc().Date;
                var nameLocked = season.NameLockDate.HasValue && today >= season.NameLockDate.Value;
                if (!nameLocked)
                {
                    await _barrioService.ChangeSeasonNameAsync(season.Id, model.Name);
                }
            }

            TempData["SuccessMessage"] = "Barrio updated successfully.";
            return RedirectToAction(nameof(Edit), new { slug });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var currentSeason = barrio.Seasons.FirstOrDefault(s => s.Id == model.SeasonId);
            if (currentSeason is not null)
            {
                model = MapToEditViewModel(barrio, currentSeason);
            }
            return View(model);
        }
    }

    // ======================================================================
    // Season opt-in
    // ======================================================================

    [Authorize]
    [HttpPost("{slug}/OptIn/{year:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OptIn(string slug, int year)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        try
        {
            await _barrioService.OptInToSeasonAsync(barrio.Id, year);
            TempData["SuccessMessage"] = $"Opted in to season {year}.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    // ======================================================================
    // Lead management
    // ======================================================================

    [Authorize]
    [HttpPost("{slug}/Leads/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLead(string slug, Guid userId)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        try
        {
            await _barrioService.AddLeadAsync(barrio.Id, userId, BarrioLeadRole.CoLead);
            TempData["SuccessMessage"] = "Co-lead added.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    [Authorize]
    [HttpPost("{slug}/Leads/Remove/{leadId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLead(string slug, Guid leadId)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        try
        {
            await _barrioService.RemoveLeadAsync(leadId);
            TempData["SuccessMessage"] = "Lead removed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    [Authorize]
    [HttpPost("{slug}/Leads/TransferPrimary")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferPrimary(string slug, Guid newPrimaryUserId)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        try
        {
            await _barrioService.TransferPrimaryLeadAsync(barrio.Id, newPrimaryUserId);
            TempData["SuccessMessage"] = "Primary lead transferred.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    // ======================================================================
    // Image management
    // ======================================================================

    [Authorize]
    [HttpPost("{slug}/Images/Upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImage(string slug, IFormFile file)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a file to upload.";
            return RedirectToAction(nameof(Edit), new { slug });
        }

        try
        {
            await _barrioService.UploadImageAsync(
                barrio.Id,
                file.OpenReadStream(),
                file.FileName,
                file.ContentType,
                file.Length);
            TempData["SuccessMessage"] = "Image uploaded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    [Authorize]
    [HttpPost("{slug}/Images/Delete/{imageId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(string slug, Guid imageId)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        try
        {
            await _barrioService.DeleteImageAsync(imageId);
            TempData["SuccessMessage"] = "Image deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    [Authorize]
    [HttpPost("{slug}/Images/Reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderImages(string slug, List<Guid> imageIds)
    {
        var barrio = await _barrioService.GetBarrioBySlugAsync(slug);
        if (barrio is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var isLead = await _barrioService.IsUserBarrioLeadAsync(user.Id, barrio.Id);
        var isCampAdmin = User.IsInRole(RoleNames.CampAdmin) || User.IsInRole(RoleNames.Admin);
        if (!isLead && !isCampAdmin) return Forbid();

        try
        {
            await _barrioService.ReorderImagesAsync(barrio.Id, imageIds);
            TempData["SuccessMessage"] = "Image order updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { slug });
    }

    // ======================================================================
    // Helper methods
    // ======================================================================

    private static BarrioSeasonData MapToSeasonData(BarrioRegisterViewModel model)
    {
        return new BarrioSeasonData(
            BlurbLong: model.BlurbLong,
            BlurbShort: model.BlurbShort,
            Languages: model.Languages,
            AcceptingMembers: model.AcceptingMembers,
            KidsWelcome: model.KidsWelcome,
            KidsVisiting: model.KidsVisiting,
            KidsAreaDescription: model.KidsAreaDescription,
            HasPerformanceSpace: model.HasPerformanceSpace,
            PerformanceTypes: model.PerformanceTypes,
            Vibes: model.Vibes,
            AdultPlayspace: model.AdultPlayspace,
            MemberCount: model.MemberCount,
            SpaceRequirement: model.SpaceRequirement,
            SoundZone: model.SoundZone,
            ContainerCount: model.ContainerCount,
            ContainerNotes: model.ContainerNotes,
            ElectricalGrid: model.ElectricalGrid);
    }

    private BarrioEditViewModel MapToEditViewModel(Barrio barrio, BarrioSeason season)
    {
        var today = _clock.GetCurrentInstant().InUtc().Date;

        return new BarrioEditViewModel
        {
            BarrioId = barrio.Id,
            Slug = barrio.Slug,
            SeasonId = season.Id,
            Year = season.Year,
            IsNameLocked = season.NameLockDate.HasValue && today >= season.NameLockDate.Value,
            Name = season.Name,
            ContactEmail = barrio.ContactEmail,
            ContactPhone = barrio.ContactPhone,
            WebOrSocialUrl = barrio.WebOrSocialUrl,
            ContactMethod = barrio.ContactMethod,
            IsSwissCamp = barrio.IsSwissCamp,
            TimesAtNowhere = barrio.TimesAtNowhere,
            BlurbLong = season.BlurbLong,
            BlurbShort = season.BlurbShort,
            Languages = season.Languages,
            AcceptingMembers = season.AcceptingMembers,
            KidsWelcome = season.KidsWelcome,
            KidsVisiting = season.KidsVisiting,
            KidsAreaDescription = season.KidsAreaDescription,
            HasPerformanceSpace = season.HasPerformanceSpace,
            PerformanceTypes = season.PerformanceTypes,
            Vibes = season.Vibes.ToList(),
            AdultPlayspace = season.AdultPlayspace,
            MemberCount = season.MemberCount,
            SpaceRequirement = season.SpaceRequirement,
            SoundZone = season.SoundZone,
            ContainerCount = season.ContainerCount,
            ContainerNotes = season.ContainerNotes,
            ElectricalGrid = season.ElectricalGrid,
            Leads = barrio.Leads
                .Where(l => l.IsActive)
                .Select(l => new BarrioLeadViewModel
                {
                    UserId = l.UserId,
                    DisplayName = l.User.DisplayName,
                    Role = l.Role
                }).ToList(),
            Images = barrio.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => new BarrioImageViewModel
                {
                    Id = i.Id,
                    Url = $"/{i.StoragePath}",
                    SortOrder = i.SortOrder
                }).ToList()
        };
    }

    private BarrioSeasonDetailViewModel MapSeasonDetail(BarrioSeason season)
    {
        var today = _clock.GetCurrentInstant().InUtc().Date;

        return new BarrioSeasonDetailViewModel
        {
            Id = season.Id,
            Year = season.Year,
            Name = season.Name,
            Status = season.Status,
            BlurbLong = season.BlurbLong,
            BlurbShort = season.BlurbShort,
            Languages = season.Languages,
            AcceptingMembers = season.AcceptingMembers,
            KidsWelcome = season.KidsWelcome,
            KidsVisiting = season.KidsVisiting,
            KidsAreaDescription = season.KidsAreaDescription,
            HasPerformanceSpace = season.HasPerformanceSpace,
            PerformanceTypes = season.PerformanceTypes,
            Vibes = season.Vibes.ToList(),
            AdultPlayspace = season.AdultPlayspace,
            MemberCount = season.MemberCount,
            SpaceRequirement = season.SpaceRequirement,
            SoundZone = season.SoundZone,
            ContainerCount = season.ContainerCount,
            ContainerNotes = season.ContainerNotes,
            ElectricalGrid = season.ElectricalGrid,
            IsNameLocked = season.NameLockDate.HasValue && today >= season.NameLockDate.Value
        };
    }
}
