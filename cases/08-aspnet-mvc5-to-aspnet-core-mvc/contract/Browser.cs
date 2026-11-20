using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Seminars.Contract;

/// <summary>One response, as a browser would have it: status, redirect target, parsed HTML.</summary>
public sealed record Page(HttpStatusCode Status, string? Location, string Raw, IHtmlDocument Html, string? ContentType)
{
    /// <summary>The text a user reads in an element: entities decoded, every kind of space made an ordinary one.</summary>
    public string Text(string selector) => Clean(Html.QuerySelector(selector)?.TextContent ?? throw new InvalidOperationException($"No element '{selector}' on the page."));
    public string[] Texts(string selector) => Html.QuerySelectorAll(selector).Select(e => Clean(e.TextContent)).ToArray();
    public bool Has(string selector) => Html.QuerySelector(selector) is not null;
    public string? Attribute(string selector, string name) => Html.QuerySelector(selector)?.GetAttribute(name);

    public static string Clean(string text) =>
        string.Join(" ", text.Replace(' ', ' ').Replace(' ', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// A minimal browser: keeps cookies, does not follow redirects by itself, and submits a form the way a browser does,
/// hidden fields included. It talks to whatever runs at SEMINARS_URL.
/// </summary>
public sealed class Browser : IDisposable
{
    public static readonly Uri BaseAddress = new(Environment.GetEnvironmentVariable("SEMINARS_URL") ?? "http://localhost:5080");

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = BaseAddress };

    public async Task<Page> GetAsync(string path) => await ReadAsync(await _http.GetAsync(path));

    /// <summary>Follows redirects until a page arrives.</summary>
    public async Task<Page> OpenAsync(string path)
    {
        var page = await GetAsync(path);
        for (var hops = 0; page.Location is not null && hops < 5; hops++) page = await GetAsync(page.Location);
        return page;
    }

    /// <summary>Fills in the form found by the selector on the given page and submits it.</summary>
    public async Task<Page> SubmitAsync(Page page, string formSelector, Dictionary<string, string> values, bool withHiddenFields = true)
    {
        var form = (IHtmlFormElement?)page.Html.QuerySelector(formSelector) ?? throw new InvalidOperationException($"No form '{formSelector}'.");
        var fields = new Dictionary<string, string>();
        foreach (var input in form.QuerySelectorAll("input[name]"))
        {
            var hidden = input.GetAttribute("type") == "hidden";
            if (hidden && !withHiddenFields && input.GetAttribute("name")!.Contains("RequestVerificationToken")) continue;
            fields[input.GetAttribute("name")!] = input.GetAttribute("value") ?? "";
        }
        foreach (var (name, value) in values) fields[name] = value;
        return await ReadAsync(await _http.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(fields)));
    }

    public async Task<(HttpStatusCode Status, string? ContentType, string Body)> GetRawAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        return (response.StatusCode, response.Content.Headers.ContentType?.ToString(), await response.Content.ReadAsStringAsync());
    }

    private static async Task<Page> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync();
            var location = response.Headers.Location is { } uri ? (uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString) : null;
            return new Page(response.StatusCode, location, raw, new HtmlParser().ParseDocument(raw), response.Content.Headers.ContentType?.ToString());
        }
    }

    public void Dispose() => _http.Dispose();
}
