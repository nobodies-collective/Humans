using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Web.Models;

namespace Humans.Web.Controllers;

[Authorize]
[Route("Human")]
public class HumanController : Controller
{
    private readonly IProfileService _profileService;
    private readonly UserManager<User> _userManager;

    public HumanController(
        IProfileService profileService,
        UserManager<User> userManager)
    {
        _profileService = profileService;
        _userManager = userManager;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> View(Guid id)
    {
        var profile = await _profileService.GetProfileAsync(id);

        if (profile == null || profile.IsSuspended)
        {
            return NotFound();
        }

        var viewer = await _userManager.GetUserAsync(User);
        if (viewer == null)
        {
            return NotFound();
        }

        var isOwnProfile = viewer.Id == id;

        // The ProfileCard ViewComponent handles all data fetching and permission checks.
        var viewModel = new ProfileViewModel
        {
            Id = profile.Id,
            UserId = id,
            DisplayName = profile.User.DisplayName,
            IsOwnProfile = isOwnProfile,
            IsApproved = profile.IsApproved,
        };

        return View("~/Views/Profile/Index.cshtml", viewModel);
    }
}
