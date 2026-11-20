using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Seminars.Web.Models;

namespace Seminars.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View(Store.Courses);
}

public class CoursesController : Controller
{
    public IActionResult Details(int id) => Store.FindCourse(id) is { } course ? View(course) : NotFound();

    // Used by the search box and by a partner site that shows our courses.
    public IActionResult Search(string? term) =>
        Json(Store.Courses.Where(c => c.Title.Contains(term ?? "", StringComparison.CurrentCultureIgnoreCase)).ToList());
}

public class RegistrationsController : Controller
{
    public IActionResult Create(int courseId) =>
        Store.FindCourse(courseId) is { } course
            ? View(new RegistrationForm { CourseId = course.Id, CourseTitle = course.Title, Seats = 1 })
            : NotFound();

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Create(RegistrationForm form)
    {
        if (Store.FindCourse(form.CourseId) is not { } course) return NotFound();
        if (!ModelState.IsValid)
        {
            form.CourseTitle = course.Title;
            return View(form);
        }

        var registration = Store.Add(new Registration
        {
            CourseId = course.Id, Name = form.Name!, Email = form.Email!, Seats = form.Seats, Budget = form.Budget,
        });
        TempData["Notice"] = $"Vielen Dank, {registration.Name}. Ihre Anmeldung ist eingegangen.";
        return RedirectToAction(nameof(Confirmation), new { id = registration.Id });
    }

    public IActionResult Confirmation(int id)
    {
        if (Store.FindRegistration(id) is not { } registration) return NotFound();
        ViewBag.Course = Store.FindCourse(registration.CourseId);
        return View(registration);
    }
}

public class AccountController : Controller
{
    public IActionResult Login(string? returnUrl) => View(new LoginForm { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (ModelState.IsValid && form is { UserName: "admin", Password: "seminar" })
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, form.UserName)], CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(new ClaimsPrincipal(identity));
            return Url.IsLocalUrl(form.ReturnUrl) ? Redirect(form.ReturnUrl) : RedirectToAction("Index", "Home");
        }
        if (ModelState.IsValid) ModelState.AddModelError("", "Benutzername oder Kennwort ist falsch.");
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}

[Authorize]
public class AdminController : Controller
{
    public IActionResult Registrations() => View(Store.AllRegistrations());
}
