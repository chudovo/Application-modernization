using System.Collections.Concurrent;
using System.Globalization;

// A document inbox as it has run for years on one Windows server: installed in a folder, started as a service,
// with everything it needs next to it. Each of the marked assumptions is true on that server.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var settings = app.Configuration;

// Assumption: the application may write into its own folder.
var logFolder = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logFolder);
var logGate = new object();
void Log(string message)
{
    lock (logGate)
        File.AppendAllText(Path.Combine(logFolder, $"inbox-{DateTime.UtcNow:yyyyMMdd}.log"), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
}

// Assumption: uploads live on the local disk, in a folder under the application.
var root = Path.Combine(AppContext.BaseDirectory, settings["Storage:Root"]!);
Directory.CreateDirectory(root);

// Assumption: there is one process and it never restarts, so the list of documents can live in memory.
var index = new ConcurrentDictionary<string, List<Document>>();
var nextId = 0;

// Assumption: the server knows the German culture and the Windows name of the German time zone.
var german = CultureInfo.GetCultureInfo("de-DE");
var berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

var fixedClock = settings["Clock:FixedUtc"];
DateTime UtcNow() => fixedClock is null ? DateTime.UtcNow : DateTime.Parse(fixedClock, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, berlin);
string Size(long bytes) => (bytes / 1024.0).ToString("N1", german) + " KB";
object View(Document d) => new { d.Id, d.Name, Size = Size(d.Bytes), Received = Local(d.ReceivedUtc).ToString("dd.MM.yyyy HH:mm", german) };

app.Use(async (context, next) =>
{
    if (context.Request.Headers["X-Api-Key"] != settings["ApiKey"])
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "A valid X-Api-Key header is required." });
        return;
    }
    await next();
});

app.MapPost("/api/mailboxes/{box}/documents", async (string box, HttpRequest request) =>
{
    var name = Uri.UnescapeDataString(request.Headers["X-File-Name"].ToString());
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "The X-File-Name header is required." });

    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    if (buffer.Length > settings.GetValue<long>("Limits:MaxBytes"))
        return Results.Json(new { error = "The document is larger than the limit." }, statusCode: StatusCodes.Status413PayloadTooLarge);

    // Stands in for the virus scan and text extraction that take a while for real documents.
    await Task.Delay(settings.GetValue<int>("Processing:DelayMs"));

    var document = new Document(Interlocked.Increment(ref nextId), name, buffer.Length, UtcNow());
    Directory.CreateDirectory(Path.Combine(root, box));
    await File.WriteAllBytesAsync(Path.Combine(root, box, $"{document.Id}_{name}"), buffer.ToArray());
    var documents = index.GetOrAdd(box, _ => []);
    lock (documents) documents.Add(document);

    Log($"stored {box}/{document.Id} {name} ({buffer.Length} bytes)");
    return Results.Created($"/api/mailboxes/{box}/documents/{document.Id}", View(document));
});

app.MapGet("/api/mailboxes/{box}/documents", (string box) =>
    index.TryGetValue(box, out var documents) ? documents.OrderByDescending(d => d.Id).Select(View) : []);

app.MapGet("/api/mailboxes/{box}/documents/{id:int}", (string box, int id) =>
{
    var document = index.TryGetValue(box, out var documents) ? documents.FirstOrDefault(d => d.Id == id) : null;
    if (document is null) return Results.NotFound(new { error = $"Document {id} was not found." });
    return Results.File(File.ReadAllBytes(Path.Combine(root, box, $"{document.Id}_{document.Name}")), "application/octet-stream", document.Name);
});

app.MapGet("/api/mailboxes/{box}/report", (string box, string date) =>
{
    var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var ofTheDay = (index.TryGetValue(box, out var documents) ? documents : []).Where(d => Local(d.ReceivedUtc).Date == day).ToList();

    // Assumption: file names are not case-sensitive and the backslash separates folders.
    var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, @"templates\dailyreport.txt"));
    var report = template
        .Replace("{date}", day.ToString("dd.MM.yyyy", german))
        .Replace("{box}", box)
        .Replace("{count}", ofTheDay.Count.ToString(german))
        .Replace("{size}", Size(ofTheDay.Sum(d => d.Bytes)))
        .Replace("{last}", ofTheDay.Count == 0 ? "-" : Local(ofTheDay.Max(d => d.ReceivedUtc)).ToString("HH:mm", german));
    return Results.Text(report.ReplaceLineEndings("\n"), "text/plain; charset=utf-8");
});

Log("started");
app.Run();

record Document(int Id, string Name, long Bytes, DateTime ReceivedUtc);
