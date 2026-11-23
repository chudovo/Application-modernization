using System.Globalization;
using System.Text.Json;

// The same inbox, built to run as a container: any number of copies, on any machine, replaced at any time.

var builder = WebApplication.CreateBuilder(args);

// Logs go to standard output, one JSON object per line. The platform collects them; the container keeps nothing.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.TimestampFormat = "O");

// The platform stops a container by sending SIGTERM and waits. Requests in flight get this long to finish.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

var app = builder.Build();
var settings = app.Configuration;
var log = app.Logger;

// Configuration comes from the environment. A secret has no default: without it the service does not start.
var apiKey = settings["ApiKey"];
if (string.IsNullOrEmpty(apiKey))
{
    log.LogCritical("ApiKey is not configured. Set the ApiKey environment variable.");
    await app.DisposeAsync();   // flushes the log before the process ends
    return 1;
}

// Documents and their descriptions live outside the container, in a mounted volume. Nothing is kept in memory, so
// every copy of the service sees the same documents and a new container continues where the old one stopped.
var store = new DocumentStore(settings["Storage:Root"]!);

// The culture and the time zone are named explicitly and the image contains the data for both (see Dockerfile).
var german = CultureInfo.GetCultureInfo("de-DE");
var berlin = TimeZoneInfo.FindSystemTimeZoneById(settings["TimeZone"]!);

var fixedClock = settings["Clock:FixedUtc"];
DateTime UtcNow() => fixedClock is null ? DateTime.UtcNow : DateTime.Parse(fixedClock, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, berlin);
string Size(long bytes) => (bytes / 1024.0).ToString("N1", german) + " KB";
object View(Document d) => new { d.Id, d.Name, Size = Size(d.Bytes), Received = Local(d.ReceivedUtc).ToString("dd.MM.yyyy HH:mm", german) };

// Liveness: the process answers. Readiness: it can do its job, which here means the volume is writable.
app.MapGet("/healthz/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/healthz/ready", () => store.IsWritable()
    ? Results.Ok(new { status = "ready" })
    : Results.Json(new { status = "storage is not writable" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/healthz"), api => api.Use(async (context, next) =>
{
    if (context.Request.Headers["X-Api-Key"] != apiKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "A valid X-Api-Key header is required." });
        return;
    }
    await next();
}));

app.MapPost("/api/mailboxes/{box}/documents", async (string box, HttpRequest request) =>
{
    var name = Uri.UnescapeDataString(request.Headers["X-File-Name"].ToString());
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "The X-File-Name header is required." });

    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    if (buffer.Length > settings.GetValue<long>("Limits:MaxBytes"))
        return Results.Json(new { error = "The document is larger than the limit." }, statusCode: StatusCodes.Status413PayloadTooLarge);

    // Stands in for the virus scan and text extraction that take a while for real documents.
    // Not tied to the request being aborted: once accepted, a document is finished even during shutdown.
    await Task.Delay(settings.GetValue<int>("Processing:DelayMs"));

    var document = await store.AddAsync(box, name, buffer.ToArray(), UtcNow());
    log.LogInformation("stored {Box}/{Id} {Name} ({Bytes} bytes)", box, document.Id, name, buffer.Length);
    return Results.Created($"/api/mailboxes/{box}/documents/{document.Id}", View(document));
});

app.MapGet("/api/mailboxes/{box}/documents", (string box) => store.List(box).OrderByDescending(d => d.Id).Select(View));

app.MapGet("/api/mailboxes/{box}/documents/{id:int}", (string box, int id) =>
    store.Find(box, id) is { } document
        ? Results.File(store.Content(box, id), "application/octet-stream", document.Name)
        : Results.NotFound(new { error = $"Document {id} was not found." }));

app.MapGet("/api/mailboxes/{box}/report", (string box, string date) =>
{
    var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var ofTheDay = store.List(box).Where(d => Local(d.ReceivedUtc).Date == day).ToList();

    // The name as it is spelled on disk, joined with the separator of the system the service runs on.
    var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", "DailyReport.txt"));
    var report = template
        .Replace("{date}", day.ToString("dd.MM.yyyy", german))
        .Replace("{box}", box)
        .Replace("{count}", ofTheDay.Count.ToString(german))
        .Replace("{size}", Size(ofTheDay.Sum(d => d.Bytes)))
        .Replace("{last}", ofTheDay.Count == 0 ? "-" : Local(ofTheDay.Max(d => d.ReceivedUtc)).ToString("HH:mm", german));
    return Results.Text(report.ReplaceLineEndings("\n"), "text/plain; charset=utf-8");
});

log.LogInformation("started, storage at {Root}", settings["Storage:Root"]);
app.Run();
return 0;

record Document(int Id, string Name, long Bytes, DateTime ReceivedUtc);

/// <summary>
/// Documents as files in a folder that several copies of the service share: for each document its content
/// (12.bin) and its description (12.json). The description file is also how a number is claimed.
/// </summary>
class DocumentStore(string root)
{
    private string Folder(string box) => Path.Combine(root, box);

    public async Task<Document> AddAsync(string box, string name, byte[] content, DateTime receivedUtc)
    {
        Directory.CreateDirectory(Folder(box));
        while (true)
        {
            var id = Directory.EnumerateFiles(Folder(box), "*.json").Select(f => int.Parse(Path.GetFileNameWithoutExtension(f))).DefaultIfEmpty(0).Max() + 1;
            var document = new Document(id, name, content.Length, receivedUtc);
            FileStream claim;
            try
            {
                // CreateNew fails if another copy of the service took this number a moment ago. Then try the next one.
                claim = new FileStream(Path.Combine(Folder(box), $"{id}.json"), FileMode.CreateNew);
            }
            catch (IOException) when (File.Exists(Path.Combine(Folder(box), $"{id}.json")))
            {
                continue;
            }
            await using (claim) await JsonSerializer.SerializeAsync(claim, document);
            await File.WriteAllBytesAsync(Path.Combine(Folder(box), $"{id}.bin"), content);
            return document;
        }
    }

    public List<Document> List(string box) =>
        !Directory.Exists(Folder(box)) ? [] :
        Directory.EnumerateFiles(Folder(box), "*.json")
            .Where(file => File.Exists(Path.ChangeExtension(file, ".bin")))   // a description without content is still being written
            .Select(file => JsonSerializer.Deserialize<Document>(File.ReadAllText(file))!)
            .ToList();

    public Document? Find(string box, int id) => List(box).FirstOrDefault(d => d.Id == id);
    public byte[] Content(string box, int id) => File.ReadAllBytes(Path.Combine(Folder(box), $"{id}.bin"));

    public bool IsWritable()
    {
        try
        {
            var probe = Path.Combine(root, $".ready-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
