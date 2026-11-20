using System.Net;
using System.Text.Json;
using Xunit;

namespace Seminars.Contract;

/// <summary>
/// What visitors, administrators and one partner site see of the seminar application. Written against HTTP and HTML
/// only, so the same tests run against the ASP.NET MVC 5 application and against the ASP.NET Core one.
/// </summary>
public class ContractTests : IDisposable
{
    private readonly Browser _browser = new();
    public void Dispose() => _browser.Dispose();

    private static Dictionary<string, string> Registration(string name = "Jürgen Müller", string email = "j.mueller@example.de", string seats = "2", string budget = "1500,00") =>
        new() { ["Name"] = name, ["Email"] = email, ["Seats"] = seats, ["Budget"] = budget };

    private async Task<Page> RegisterAsync(Dictionary<string, string> values, int courseId = 2) =>
        await _browser.SubmitAsync(await _browser.GetAsync($"/Registrations/Create?courseId={courseId}"), "#registration", values);

    private static int Count(Page page) => int.Parse(page.Text("#summary").Replace("Anmeldungen bisher: ", ""));

    // ---------- pages ----------

    [Fact]
    public async Task Home_page_lists_the_courses_with_german_prices_and_dates()
    {
        var page = await _browser.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Kursübersicht - Seminare", Page.Clean(page.Html.Title!));
        Assert.Equal(["Einführung in C#", "Größere Refactorings planen", "SQL für Entwickler <Aufbaukurs>"], page.Texts("#courses .title"));
        Assert.Equal(["1.249,50 €", "890,00 €", "1.500,00 €"], page.Texts("#courses .price"));
        Assert.Equal(["Montag, 12. Januar 2026", "Dienstag, 3. Februar 2026", "Montag, 9. März 2026"], page.Texts("#courses .start"));
    }

    [Fact]
    public async Task Course_links_lead_to_the_detail_page()
    {
        var home = await _browser.GetAsync("/");
        Assert.Equal("/Courses/Details/2", home.Attribute("#courses tr:nth-child(2) .title a", "href"));

        var page = await _browser.GetAsync("/Courses/Details/2");

        Assert.Equal("Größere Refactorings planen", page.Text("h1"));
        Assert.Equal("Preis: 890,00 EUR", page.Text("#price"));
        Assert.Equal("Beginn: 03.02.2026", page.Text("#start"));
        Assert.Equal("/Registrations/Create?courseId=2", page.Attribute("#register", "href"));
    }

    [Theory]
    [InlineData("/courses/details/2")]   // routes ignore case
    [InlineData("/Courses/Details/2/")]  // and a trailing slash
    public async Task Detail_page_is_reachable_under_the_address_variants_people_have_bookmarked(string path)
    {
        Assert.Equal("Größere Refactorings planen", (await _browser.GetAsync(path)).Text("h1"));
    }

    [Fact]
    public async Task Markup_in_data_is_shown_as_text_not_interpreted()
    {
        var page = await _browser.GetAsync("/Courses/Details/3");

        Assert.Equal("SQL für Entwickler <Aufbaukurs>", page.Text("h1"));
        Assert.Contains("&lt;Aufbaukurs&gt;", page.Raw);
    }

    [Fact]
    public async Task Unknown_course_is_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync("/Courses/Details/99")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync("/Registrations/Create?courseId=99")).Status);
    }

    [Fact]
    public async Task Every_page_shows_the_number_of_registrations()
    {
        var before = Count(await _browser.GetAsync("/"));

        await RegisterAsync(Registration());

        Assert.Equal(before + 1, Count(await _browser.GetAsync("/")));
        Assert.Equal(before + 1, Count(await _browser.GetAsync("/Courses/Details/1")));
    }

    // ---------- the registration form ----------

    [Fact]
    public async Task Registration_form_carries_what_client_side_validation_needs()
    {
        var page = await _browser.GetAsync("/Registrations/Create?courseId=2");

        Assert.Equal("Anmeldung: Größere Refactorings planen", page.Text("h1"));
        Assert.Equal("/Registrations/Create", page.Attribute("#registration", "action"));
        Assert.Equal("2", page.Attribute("#registration input[name=CourseId]", "value"));
        Assert.Equal("1", page.Attribute("#registration input[name=Seats]", "value"));
        Assert.Equal("Bitte geben Sie Ihren Namen an.", page.Attribute("#registration input[name=Name]", "data-val-required"));
        Assert.Equal("Es können 1 bis 5 Plätze gebucht werden.", page.Attribute("#registration input[name=Seats]", "data-val-range"));
        Assert.True(page.Has("#registration input[name=__RequestVerificationToken]"));
    }

    [Fact]
    public async Task Invalid_registration_is_shown_again_with_messages_and_the_entered_values()
    {
        var page = await RegisterAsync(Registration(name: "", email: "keine-adresse", seats: "9"));

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(
            ["Bitte geben Sie Ihren Namen an.", "Die E-Mail-Adresse ist ungültig.", "Es können 1 bis 5 Plätze gebucht werden."],
            page.Texts("#registration .field-validation-error"));
        Assert.Equal("keine-adresse", page.Attribute("#registration input[name=Email]", "value"));
        Assert.Equal("9", page.Attribute("#registration input[name=Seats]", "value"));
        Assert.Equal("Anmeldung: Größere Refactorings planen", page.Text("h1"));
    }

    [Fact]
    public async Task Valid_registration_redirects_to_a_confirmation_that_shows_the_notice_once()
    {
        var posted = await RegisterAsync(Registration());

        Assert.Equal(HttpStatusCode.Found, posted.Status);
        Assert.StartsWith("/Registrations/Confirmation/", posted.Location);

        var confirmation = await _browser.GetAsync(posted.Location!);
        Assert.Equal("Vielen Dank, Jürgen Müller. Ihre Anmeldung ist eingegangen.", confirmation.Text("#notice"));
        Assert.Equal("Größere Refactorings planen", confirmation.Text("#course"));
        Assert.Equal("Jürgen Müller", confirmation.Text("#name"));
        Assert.Equal("2", confirmation.Text("#seats"));

        // The notice belongs to the redirect. A reload does not show it again.
        Assert.False((await _browser.GetAsync(posted.Location!)).Has("#notice"));
    }

    [Theory]
    [InlineData("1500,00", "1.500,00 EUR")]   // a decimal comma, the German way
    [InlineData("750,5", "750,50 EUR")]
    [InlineData("2000", "2.000,00 EUR")]
    public async Task Amounts_are_read_the_way_german_users_type_them(string typed, string shown)
    {
        var posted = await RegisterAsync(Registration(budget: typed));

        Assert.Equal(HttpStatusCode.Found, posted.Status);
        Assert.Equal(shown, (await _browser.GetAsync(posted.Location!)).Text("#budget"));
    }

    [Theory]
    [InlineData("750.50")]     // a decimal point: on a German system this is not seven hundred and fifty
    [InlineData("1.500,00")]   // a thousands separator
    public async Task An_amount_the_application_cannot_read_is_refused_not_guessed(string typed)
    {
        var page = await RegisterAsync(Registration(budget: typed));

        // The form comes back with one message and the text as it was typed. Nothing like 75050 is stored.
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal([$"The value '{typed}' is not valid for Budget."], page.Texts("#registration .field-validation-error"));
        Assert.Equal(typed, page.Attribute("#registration input[name=Budget]", "value"));
    }

    [Fact]
    public async Task Registration_without_the_anti_forgery_token_is_refused_and_stores_nothing()
    {
        var before = Count(await _browser.GetAsync("/"));
        var form = await _browser.GetAsync("/Registrations/Create?courseId=2");

        var posted = await _browser.SubmitAsync(form, "#registration", Registration(), withHiddenFields: false);

        Assert.True((int)posted.Status >= 400, $"status was {(int)posted.Status}");
        Assert.Equal(before, Count(await _browser.GetAsync("/")));
    }

    // ---------- the administration area ----------

    [Fact]
    public async Task Administration_area_sends_anonymous_visitors_to_the_login_page_and_back()
    {
        var page = await _browser.GetAsync("/Admin/Registrations");

        Assert.Equal(HttpStatusCode.Found, page.Status);
        var location = new Uri(Browser.BaseAddress, page.Location!);
        Assert.Equal("/Account/Login", location.AbsolutePath);
        Assert.Equal("?ReturnUrl=/Admin/Registrations", Uri.UnescapeDataString(location.Query));
    }

    [Fact]
    public async Task Wrong_password_shows_a_message_and_does_not_sign_in()
    {
        var login = await _browser.OpenAsync("/Admin/Registrations");

        var page = await _browser.SubmitAsync(login, "#login", new() { ["UserName"] = "admin", ["Password"] = "falsch" });

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Benutzername oder Kennwort ist falsch.", page.Text(".validation-summary-errors"));
        Assert.Equal(HttpStatusCode.Found, (await _browser.GetAsync("/Admin/Registrations")).Status);
    }

    [Fact]
    public async Task Administrator_signs_in_sees_the_registrations_and_signs_out()
    {
        await RegisterAsync(Registration(name: "Zoë Großmann", email: "zoe@example.de", budget: "980,00"));
        var login = await _browser.OpenAsync("/Admin/Registrations");

        var signedIn = await _browser.SubmitAsync(login, "#login", new() { ["UserName"] = "admin", ["Password"] = "seminar" });
        Assert.Equal(HttpStatusCode.Found, signedIn.Status);
        Assert.Equal("/Admin/Registrations", signedIn.Location);

        var admin = await _browser.GetAsync("/Admin/Registrations");
        Assert.Equal(HttpStatusCode.OK, admin.Status);
        Assert.Contains("Zoë Großmann", admin.Texts("#registrations .name"));
        Assert.Contains("980,00", admin.Texts("#registrations .budget"));
        Assert.Equal("Angemeldet als admin", admin.Text("#user"));

        var signedOut = await _browser.SubmitAsync(admin, "#logout", []);
        Assert.Equal(HttpStatusCode.Found, signedOut.Status);
        Assert.Equal(HttpStatusCode.Found, (await _browser.GetAsync("/Admin/Registrations")).Status);
    }

    [Fact]
    public async Task Login_does_not_redirect_to_another_site()
    {
        var login = await _browser.GetAsync("/Account/Login?ReturnUrl=https://example.org/");

        var signedIn = await _browser.SubmitAsync(login, "#login", new() { ["UserName"] = "admin", ["Password"] = "seminar" });

        Assert.Equal(HttpStatusCode.Found, signedIn.Status);
        Assert.Equal("/", signedIn.Location);
    }

    // ---------- the JSON a partner site reads ----------

    [Fact]
    public async Task Course_search_returns_json_in_the_shape_the_partner_site_parses()
    {
        var (status, contentType, body) = await _browser.GetRawAsync("/Courses/Search?term=refactor");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("application/json; charset=utf-8", contentType);

        var course = Assert.Single(JsonDocument.Parse(body).RootElement.EnumerateArray());
        Assert.Equal(["Id", "Title", "Description", "Price", "Start"], course.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, course.GetProperty("Id").GetInt32());
        Assert.Equal("Größere Refactorings planen", course.GetProperty("Title").GetString());
        Assert.Equal(890.00m, course.GetProperty("Price").GetDecimal());
        // The format the old JSON serializer wrote, and the partner's script reads.
        Assert.Equal("/Date(1770109200000)/", course.GetProperty("Start").GetString());
    }
}
