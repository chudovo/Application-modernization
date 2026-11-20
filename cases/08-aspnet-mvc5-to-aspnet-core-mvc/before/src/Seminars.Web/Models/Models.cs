using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Seminars.Web.Models
{
    public class Course
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public DateTime Start { get; set; }
    }

    public class Registration
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public int Seats { get; set; }
        public decimal Budget { get; set; }
    }

    public class RegistrationForm
    {
        public int CourseId { get; set; }
        public string CourseTitle { get; set; }

        [Required(ErrorMessage = "Bitte geben Sie Ihren Namen an.")]
        [StringLength(80)]
        public string Name { get; set; }

        [Required(ErrorMessage = "Bitte geben Sie eine E-Mail-Adresse an.")]
        [EmailAddress(ErrorMessage = "Die E-Mail-Adresse ist ungültig.")]
        public string Email { get; set; }

        [Range(1, 5, ErrorMessage = "Es können 1 bis 5 Plätze gebucht werden.")]
        public int Seats { get; set; }

        [Range(typeof(decimal), "0", "100000", ErrorMessage = "Das Budget muss zwischen 0 und 100.000 liegen.")]
        public decimal Budget { get; set; }
    }

    public class LoginForm
    {
        [Required(ErrorMessage = "Bitte geben Sie den Benutzernamen an.")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "Bitte geben Sie das Kennwort an.")]
        public string Password { get; set; }

        public string ReturnUrl { get; set; }
    }

    /// <summary>Stands in for the database. The case is about the web layer.</summary>
    public static class Store
    {
        private static readonly object Gate = new object();
        private static readonly List<Registration> Registrations = new List<Registration>();

        public static readonly IReadOnlyList<Course> Courses = new[]
        {
            new Course { Id = 1, Title = "Einführung in C#", Description = "Drei Tage für Umsteiger: Typen, LINQ, async und Tests.",
                         Price = 1249.50m, Start = new DateTime(2026, 1, 12, 9, 0, 0, DateTimeKind.Utc) },
            new Course { Id = 2, Title = "Größere Refactorings planen", Description = "Wie man Altcode in kleinen, überprüfbaren Schritten ändert.",
                         Price = 890.00m, Start = new DateTime(2026, 2, 3, 9, 0, 0, DateTimeKind.Utc) },
            new Course { Id = 3, Title = "SQL für Entwickler <Aufbaukurs>", Description = "Indizes, Sperren und Ausführungspläne lesen.",
                         Price = 1500.00m, Start = new DateTime(2026, 3, 9, 9, 0, 0, DateTimeKind.Utc) },
        };

        public static Course FindCourse(int id) { return Courses.FirstOrDefault(c => c.Id == id); }

        public static Registration Add(Registration registration)
        {
            lock (Gate)
            {
                registration.Id = Registrations.Count + 1;
                Registrations.Add(registration);
                return registration;
            }
        }

        public static Registration FindRegistration(int id) { lock (Gate) return Registrations.FirstOrDefault(r => r.Id == id); }
        public static List<Registration> AllRegistrations() { lock (Gate) return Registrations.ToList(); }
        public static int Count() { lock (Gate) return Registrations.Count; }
    }
}
