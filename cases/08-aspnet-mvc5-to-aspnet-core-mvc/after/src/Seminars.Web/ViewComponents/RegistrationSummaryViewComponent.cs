using Microsoft.AspNetCore.Mvc;
using Seminars.Web.Models;

namespace Seminars.Web.ViewComponents;

/// <summary>Replaces the child action Registrations/Summary, which the layout rendered with Html.Action.</summary>
public class RegistrationSummaryViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View(Store.Count());
}
