using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Inbox.Tests;

/// <summary>
/// What the clients of the inbox rely on, over HTTP. The same scenarios run against the service started as a plain
/// process on Windows and against the container. The service under test is at INBOX_URL and was started with the
/// clock fixed at 2026-07-01 22:30 UTC, which is half past midnight on 2 July in Germany.
/// </summary>
[Trait("Kind", "Scenario")]
public class Scenarios : IDisposable
{
    private readonly HttpClient _http = InboxClient.Create(Environment.GetEnvironmentVariable("INBOX_URL") ?? "http://localhost:5090");
    private readonly string _box = "box-" + Guid.NewGuid().ToString("N")[..8];
    public void Dispose() => _http.Dispose();

    private Task<HttpResponseMessage> UploadAsync(string name, byte[] content) => InboxClient.UploadAsync(_http, _box, name, content);
    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Uploaded_document_is_described_with_german_size_and_local_time()
    {
        var response = await UploadAsync("Angebot.txt", new byte[1536]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await Json(response);
        Assert.Equal("Angebot.txt", document.GetProperty("name").GetString());
        Assert.Equal("1,5 KB", document.GetProperty("size").GetString());
        // 22:30 UTC on 1 July is 00:30 on 2 July in Germany, in summer time.
        Assert.Equal("02.07.2026 00:30", document.GetProperty("received").GetString());
        Assert.EndsWith($"/api/mailboxes/{_box}/documents/{document.GetProperty("id").GetInt32()}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Document_is_downloaded_byte_for_byte_under_its_name()
    {
        var content = Encoding.UTF8.GetBytes("Sehr geehrte Damen und Herren,\r\nanbei die Übersicht für März.\r\n");
        var uploaded = await Json(await UploadAsync("Übersicht März.txt", content));

        var response = await _http.GetAsync($"/api/mailboxes/{_box}/documents/{uploaded.GetProperty("id").GetInt32()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("Übersicht März.txt", response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Mailbox_lists_its_documents_newest_first()
    {
        await UploadAsync("eins.txt", new byte[10]);
        await UploadAsync("zwei.txt", new byte[2048]);

        var list = await Json(await _http.GetAsync($"/api/mailboxes/{_box}/documents"));

        Assert.Equal(["zwei.txt", "eins.txt"], list.EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        Assert.Equal("2,0 KB", list[0].GetProperty("size").GetString());
    }

    [Fact]
    public async Task Another_mailbox_does_not_see_the_documents()
    {
        await UploadAsync("eins.txt", new byte[10]);

        Assert.Equal(0, (await Json(await _http.GetAsync($"/api/mailboxes/{_box}-other/documents"))).GetArrayLength());
    }

    [Fact]
    public async Task Daily_report_counts_by_the_german_calendar_day()
    {
        await UploadAsync("eins.txt", new byte[1024]);
        await UploadAsync("zwei.txt", new byte[2048]);

        var report = await (await _http.GetAsync($"/api/mailboxes/{_box}/report?date=2026-07-02")).Content.ReadAsStringAsync();

        Assert.Equal($"Tagesbericht 02.07.2026\nPostfach: {_box}\nDokumente: 2\nGesamtgröße: 3,0 KB\nLetzter Eingang: 00:30\n", report);
    }

    [Fact]
    public async Task Daily_report_for_the_utc_day_is_empty()
    {
        await UploadAsync("eins.txt", new byte[1024]);

        var report = await (await _http.GetAsync($"/api/mailboxes/{_box}/report?date=2026-07-01")).Content.ReadAsStringAsync();

        Assert.Contains("Dokumente: 0\n", report);
        Assert.Contains("Letzter Eingang: -\n", report);
    }

    [Fact]
    public async Task Unknown_document_is_a_404()
    {
        var response = await _http.GetAsync($"/api/mailboxes/{_box}/documents/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("""{"error":"Document 999999 was not found."}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Document_over_the_size_limit_is_a_413_and_is_not_stored()
    {
        var response = await UploadAsync("zu-gross.bin", new byte[1024 * 1024 + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, (await Json(await _http.GetAsync($"/api/mailboxes/{_box}/documents"))).GetArrayLength());
    }

    [Fact]
    public async Task Upload_without_a_file_name_is_a_400()
    {
        var response = await _http.PostAsync($"/api/mailboxes/{_box}/documents", new ByteArrayContent(new byte[10]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Request_without_the_api_key_is_a_401()
    {
        using var anonymous = new HttpClient { BaseAddress = _http.BaseAddress };

        var response = await anonymous.GetAsync($"/api/mailboxes/{_box}/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"A valid X-Api-Key header is required."}""", await response.Content.ReadAsStringAsync());
    }
}

public static class InboxClient
{
    public static HttpClient Create(string baseAddress)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-Api-Key", "demo-key");
        return http;
    }

    public static Task<HttpResponseMessage> UploadAsync(HttpClient http, string box, string name, byte[] content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/mailboxes/{box}/documents") { Content = new ByteArrayContent(content) };
        request.Headers.Add("X-File-Name", Uri.EscapeDataString(name));
        return http.SendAsync(request);
    }
}
