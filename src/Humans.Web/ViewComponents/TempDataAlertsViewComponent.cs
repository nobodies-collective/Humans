using Microsoft.AspNetCore.Mvc;

namespace Humans.Web.ViewComponents;

public class TempDataAlertsViewComponent : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var alerts = new List<TempDataAlert>();

        if (TempData["SuccessMessage"] is string success)
            alerts.Add(new TempDataAlert("success", success));

        if (TempData["ErrorMessage"] is string error)
            alerts.Add(new TempDataAlert("danger", error));

        if (TempData["InfoMessage"] is string info)
            alerts.Add(new TempDataAlert("info", info));

        return View(alerts);
    }

    public record TempDataAlert(string CssClass, string Message);
}
