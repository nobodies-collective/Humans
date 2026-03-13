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

[Authorize(Roles = $"{RoleNames.CampAdmin},{RoleNames.Admin}")]
[Route("Barrios/Admin")]
public class BarrioAdminController : Controller
{
    private readonly IBarrioService _barrioService;
    private readonly UserManager<User> _userManager;

    public BarrioAdminController(IBarrioService barrioService, UserManager<User> userManager)
    {
        _barrioService = barrioService;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var settings = await _barrioService.GetSettingsAsync();
        var allBarrios = await _barrioService.GetBarriosForYearAsync(settings.PublicYear);
        var pendingSeasons = await _barrioService.GetPendingSeasonsAsync();

        var vm = new BarrioAdminViewModel
        {
            PublicYear = settings.PublicYear,
            OpenSeasons = settings.OpenSeasons,
            TotalBarrios = allBarrios.Count,
            ActiveBarrios = allBarrios.Count(b => b.Seasons.Any(s =>
                s.Year == settings.PublicYear && s.Status == BarrioSeasonStatus.Active)),
            PendingBarrios = pendingSeasons.Select(s => new BarrioCardViewModel
            {
                Id = s.BarrioId,
                Slug = s.Barrio?.Slug ?? string.Empty,
                Name = s.Name,
                BlurbShort = s.BlurbShort,
                Status = s.Status
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost("Approve/{seasonId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid seasonId, string? notes)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        try
        {
            await _barrioService.ApproveSeasonAsync(seasonId, user.Id, notes);
            TempData["SuccessMessage"] = "Season approved.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Reject/{seasonId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid seasonId, string notes)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(notes))
        {
            TempData["ErrorMessage"] = "Rejection notes are required.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _barrioService.RejectSeasonAsync(seasonId, user.Id, notes);
            TempData["SuccessMessage"] = "Season rejected.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("OpenSeason/{year:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenSeason(int year)
    {
        await _barrioService.OpenSeasonAsync(year);
        TempData["SuccessMessage"] = $"Season {year} opened for registration.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("CloseSeason/{year:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseSeason(int year)
    {
        await _barrioService.CloseSeasonAsync(year);
        TempData["SuccessMessage"] = $"Season {year} closed for registration.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("SetPublicYear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPublicYear(int year)
    {
        await _barrioService.SetPublicYearAsync(year);
        TempData["SuccessMessage"] = $"Public year set to {year}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("SetNameLockDate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetNameLockDate(int year, LocalDate lockDate)
    {
        await _barrioService.SetNameLockDateAsync(year, lockDate);
        TempData["SuccessMessage"] = $"Name lock date for {year} set to {lockDate}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Delete/{barrioId:guid}")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Delete(Guid barrioId)
    {
        try
        {
            await _barrioService.DeleteBarrioAsync(barrioId);
            TempData["SuccessMessage"] = "Barrio deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
