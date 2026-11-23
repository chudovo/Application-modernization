using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Inbox.Tests;

/// <summary>
/// What a container platform expects of a service, beyond answering requests: start under restrictions, give way when
/// told to stop, lose nothing when replaced, run in several copies. Needs Docker. The image under test is INBOX_IMAGE.
/// </summary>
[Trait("Kind", "Container")]
public class ContainerTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string Image = Environment.GetEnvironmentVariable("INBOX_IMAGE") ?? "inbox:after";
    private readonly string _run = "inbox-test-" + Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _containers = [];
    private string Volume => _run + "-data";

    // How a careful platform runs a service: read-only file system, no extra privileges, data in a volume.
    // INBOX_RESTRICTIONS=off drops them, to see how far an image gets without.
    private static readonly string Restrictions = Environment.GetEnvironmentVariable("INBOX_RESTRICTIONS") == "off"
        ? "" : "--read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges";

    private async Task<Running> StartAsync(string extra = "", string volumeMode = "", bool withApiKey = true, bool waitUntilLive = true)
    {
        var name = $"{_run}-{_containers.Count + 1}";
        _containers.Add(name);
        var key = withApiKey ? "-e ApiKey=demo-key" : "";
        var (exit, text) = Docker($"run -d --name {name} {Restrictions} {key} -e Clock__FixedUtc=2026-07-01T22:30:00Z -v {Volume}:/data{volumeMode} -p 127.0.0.1::8080 {extra} {Image}");
        Assert.True(exit == 0, text);

        var running = new Running(name, null!);
        if (!waitUntilLive) return running;

        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(200);
            if (Docker($"inspect -f {{{{.State.Running}}}} {name}").Output.Trim() != "true") break;
            var port = Docker($"port {name} 8080/tcp").Output.Trim().Split(':').Last();
            if (port.Length == 0) continue;
            var http = InboxClient.Create($"http://127.0.0.1:{port}");
            try
            {
                // Any answer means the process is listening.
                await http.GetAsync("/api/mailboxes/probe/documents");
                return running with { Http = http };
            }
            catch (HttpRequestException) { http.Dispose(); }
        }
        Assert.Fail($"The container did not come up.\n{Docker($"logs {name}").Output}");
        return running;
    }

    private sealed record Running(string Name, HttpClient Http);

    private static (int Exit, string Output) Docker(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var error = process.StandardError.ReadToEndAsync();
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, text + error.Result);
    }

    public void Dispose()
    {
        foreach (var name in _containers) Docker($"rm -f {name}");
        Docker($"volume rm -f {Volume}");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Starts_on_a_read_only_file_system_without_privileges_and_reports_ready()
    {
        var service = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await service.Http.GetAsync("/healthz/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await InboxClient.UploadAsync(service.Http, "box", "eins.txt", new byte[100])).StatusCode);
    }

    [Fact]
    public void Image_does_not_run_as_root()
    {
        var user = Docker($"image inspect -f {{{{.Config.User}}}} {Image}").Output.Trim();
        output.WriteLine($"IMAGE {Image}: user '{user}', size {long.Parse(Docker($"image inspect -f {{{{.Size}}}} {Image}").Output.Trim()) / 1_000_000} MB");

        Assert.False(user is "" or "0" or "root", $"the image runs as '{user}'");
    }

    [Fact]
    public async Task Health_endpoints_need_no_api_key()
    {
        var service = await StartAsync();
        using var anonymous = new HttpClient { BaseAddress = service.Http.BaseAddress };

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/healthz/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/healthz/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/mailboxes/box/documents")).StatusCode);
    }

    [Fact]
    public async Task Is_live_but_not_ready_when_the_storage_cannot_be_written()
    {
        var service = await StartAsync(volumeMode: ":ro");

        Assert.Equal(HttpStatusCode.OK, (await service.Http.GetAsync("/healthz/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await service.Http.GetAsync("/healthz/ready")).StatusCode);
    }

    [Fact]
    public async Task Documents_survive_the_replacement_of_the_container()
    {
        var first = await StartAsync();
        var uploaded = await Json(await InboxClient.UploadAsync(first.Http, "box", "bleibt.txt", "noch da"u8.ToArray()));
        Docker($"rm -f {first.Name}");

        var second = await StartAsync();

        var list = await Json(await second.Http.GetAsync("/api/mailboxes/box/documents"));
        Assert.Equal(["bleibt.txt"], list.EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        Assert.Equal("noch da", await (await second.Http.GetAsync($"/api/mailboxes/box/documents/{uploaded.GetProperty("id").GetInt32()}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Two_copies_of_the_service_serve_the_same_documents()
    {
        var a = await StartAsync();
        var b = await StartAsync();

        // Uploads arrive at both copies at the same time, as behind a load balancer.
        var uploads = Enumerable.Range(0, 20).Select(n => InboxClient.UploadAsync(n % 2 == 0 ? a.Http : b.Http, "box", $"dokument-{n}.txt", new byte[10 + n]));
        var ids = new List<int>();
        foreach (var response in await Task.WhenAll(uploads))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await Json(response)).GetProperty("id").GetInt32());
        }

        Assert.Equal(20, ids.Distinct().Count());
        foreach (var copy in new[] { a, b })
            Assert.Equal(20, (await Json(await copy.Http.GetAsync("/api/mailboxes/box/documents"))).GetArrayLength());
    }

    [Fact]
    public async Task An_upload_in_progress_is_finished_when_the_container_is_told_to_stop()
    {
        var service = await StartAsync(extra: "-e Processing__DelayMs=4000");

        var upload = InboxClient.UploadAsync(service.Http, "box", "unterwegs.txt", new byte[500]);
        await Task.Delay(1000);
        var stopwatch = Stopwatch.StartNew();
        Docker($"stop -t 20 {service.Name}");   // SIGTERM, then 20 seconds of patience, as a platform does
        output.WriteLine($"STOP {Image}: docker stop returned after {stopwatch.Elapsed.TotalSeconds:0.0} s");

        Assert.Equal(HttpStatusCode.Created, (await upload).StatusCode);

        var next = await StartAsync();
        Assert.Equal(1, (await Json(await next.Http.GetAsync("/api/mailboxes/box/documents"))).GetArrayLength());
    }

    [Fact]
    public async Task Logs_are_json_lines_on_standard_output()
    {
        var service = await StartAsync();
        await InboxClient.UploadAsync(service.Http, "box", "eins.txt", new byte[100]);

        var lines = Docker($"logs {service.Name}").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.All(lines, line => JsonDocument.Parse(line));
        Assert.Contains(lines, line => line.Contains("stored box/1 eins.txt"));
    }

    [Fact]
    public async Task Does_not_start_without_its_secret()
    {
        var service = await StartAsync(withApiKey: false, waitUntilLive: false);

        // It has ten seconds to give up. A service that keeps running without its secret fails here.
        var exit = "still running";
        for (var attempt = 0; attempt < 50 && exit == "still running"; attempt++)
        {
            await Task.Delay(200);
            var state = Docker($"inspect -f {{{{.State.Running}}}}:{{{{.State.ExitCode}}}} {service.Name}").Output.Trim().Split(':');
            if (state[0] == "false") exit = state[1];
        }

        Assert.NotEqual("still running", exit);
        Assert.NotEqual("0", exit);
        Assert.Contains("ApiKey is not configured", Docker($"logs {service.Name}").Output);
    }
}
