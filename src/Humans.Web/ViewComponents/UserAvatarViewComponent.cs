using Microsoft.AspNetCore.Mvc;

namespace Humans.Web.ViewComponents;

public class UserAvatarViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string? profilePictureUrl,
        string? displayName,
        int size = 40,
        string? cssClass = null,
        string bgColor = "bg-secondary")
    {
        var initial = string.IsNullOrEmpty(displayName) ? "?" : displayName[0].ToString();

        // Scale font size proportionally: roughly size * 0.4 as rem, capped reasonably
        var fontRem = Math.Round(size / 100.0 * 2.0, 1);
        if (fontRem < 0.7) fontRem = 0.7;

        ViewBag.ProfilePictureUrl = profilePictureUrl;
        ViewBag.Initial = initial;
        ViewBag.Size = size;
        ViewBag.CssClass = cssClass;
        ViewBag.BgColor = bgColor;
        ViewBag.FontRem = fontRem;

        return View();
    }
}
