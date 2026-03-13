using Humans.Application.Interfaces;
using Humans.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Humans.Web.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/barrios")]
public class BarrioApiController : ControllerBase
{
    private readonly IBarrioService _barrioService;

    public BarrioApiController(IBarrioService barrioService)
    {
        _barrioService = barrioService;
    }

    [HttpGet("{year:int}")]
    public async Task<IActionResult> GetBarrios(int year)
    {
        var barrios = await _barrioService.GetBarriosForYearAsync(year);

        var result = barrios.Select(b =>
        {
            var season = b.Seasons.FirstOrDefault(s => s.Year == year);
            var firstImage = b.Images.OrderBy(i => i.SortOrder).FirstOrDefault();
            return new
            {
                b.Id,
                b.Slug,
                Name = season?.Name ?? b.Slug,
                BlurbShort = season?.BlurbShort ?? string.Empty,
                BlurbLong = season?.BlurbLong ?? string.Empty,
                ImageUrl = firstImage != null ? $"/{firstImage.StoragePath}" : (string?)null,
                Vibes = season?.Vibes ?? new List<BarrioVibe>(),
                AcceptingMembers = season?.AcceptingMembers ?? YesNoMaybe.No,
                KidsWelcome = season?.KidsWelcome ?? YesNoMaybe.No,
                SoundZone = season?.SoundZone,
                Status = season?.Status ?? BarrioSeasonStatus.Pending,
                b.TimesAtNowhere,
                b.IsSwissCamp,
                b.ContactEmail,
                b.ContactMethod,
                b.WebOrSocialUrl,
                Leads = b.Leads
                    .Where(l => l.IsActive)
                    .Select(l => new
                    {
                        l.User.DisplayName,
                        Role = l.Role.ToString()
                    }).ToList()
            };
        }).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return Ok(result);
    }

    [HttpGet("{year:int}/placement")]
    public async Task<IActionResult> GetPlacement(int year)
    {
        var barrios = await _barrioService.GetBarriosForYearAsync(year);

        var result = barrios
            .Select(b =>
            {
                var season = b.Seasons.FirstOrDefault(s => s.Year == year);
                if (season is null) return null;
                return new
                {
                    b.Id,
                    b.Slug,
                    season.Name,
                    season.MemberCount,
                    season.SpaceRequirement,
                    season.SoundZone,
                    season.ContainerCount,
                    season.ContainerNotes,
                    season.ElectricalGrid,
                    Status = season.Status.ToString()
                };
            })
            .Where(x => x is not null)
            .OrderBy(x => x!.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(result);
    }
}
