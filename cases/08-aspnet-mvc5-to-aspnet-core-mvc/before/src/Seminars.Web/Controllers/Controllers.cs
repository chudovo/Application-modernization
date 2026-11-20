using System.Linq;
using System.Web.Mvc;
using System.Web.Security;
using Seminars.Web.Models;

namespace Seminars.Web.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index() { return View(Store.Courses); }
    }

    public class CoursesController : Controller
    {
        public ActionResult Details(int id)
        {
            var course = Store.FindCourse(id);
            if (course == null) return HttpNotFound();
            return View(course);
        }

        // Used by the search box and by a partner site that shows our courses.
        public ActionResult Search(string term)
        {
            var found = Store.Courses.Where(c => c.Title.ToLower().Contains((term ?? "").ToLower())).ToList();
            return Json(found, JsonRequestBehavior.AllowGet);
        }
    }

    public class RegistrationsController : Controller
    {
        public ActionResult Create(int courseId)
        {
            var course = Store.FindCourse(courseId);
            if (course == null) return HttpNotFound();
            return View(new RegistrationForm { CourseId = course.Id, CourseTitle = course.Title, Seats = 1 });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Create(RegistrationForm form)
        {
            var course = Store.FindCourse(form.CourseId);
            if (course == null) return HttpNotFound();
            if (!ModelState.IsValid)
            {
                form.CourseTitle = course.Title;
                return View(form);
            }

            var registration = Store.Add(new Registration
            {
                CourseId = course.Id, Name = form.Name, Email = form.Email, Seats = form.Seats, Budget = form.Budget
            });
            TempData["Notice"] = "Vielen Dank, " + registration.Name + ". Ihre Anmeldung ist eingegangen.";
            return RedirectToAction("Confirmation", new { id = registration.Id });
        }

        public ActionResult Confirmation(int id)
        {
            var registration = Store.FindRegistration(id);
            if (registration == null) return HttpNotFound();
            ViewBag.Course = Store.FindCourse(registration.CourseId);
            return View(registration);
        }

        // Rendered inside the layout of every page.
        [ChildActionOnly]
        public ActionResult Summary() { return PartialView("_Summary", Store.Count()); }
    }

    public class AccountController : Controller
    {
        public ActionResult Login(string returnUrl) { return View(new LoginForm { ReturnUrl = returnUrl }); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Login(LoginForm form)
        {
            if (ModelState.IsValid && form.UserName == "admin" && form.Password == "seminar")
            {
                FormsAuthentication.SetAuthCookie(form.UserName, false);
                if (Url.IsLocalUrl(form.ReturnUrl)) return Redirect(form.ReturnUrl);
                return RedirectToAction("Index", "Home");
            }
            if (ModelState.IsValid) ModelState.AddModelError("", "Benutzername oder Kennwort ist falsch.");
            return View(form);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Index", "Home");
        }
    }

    [Authorize]
    public class AdminController : Controller
    {
        public ActionResult Registrations() { return View(Store.AllRegistrations()); }
    }
}
