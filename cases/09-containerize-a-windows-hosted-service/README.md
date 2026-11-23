# Case 09: Containerizing a service that grew up on one Windows server

A document inbox that has run for years as a service on one Windows server, and the same inbox built to run as a
container: under restrictions, in several copies, replaced at any time.

Two sets of tests. Ten scenarios describe what clients see over HTTP; they run against the old service as a process
on Windows and against the container. Nine container tests describe what a platform expects of a service beyond
answering requests; they run against the image.

| | Before | After |
|---|---|---|
| Runs as | a process on one Windows server | a container, non-root, read-only file system |
| Configuration | `appsettings.json` next to the binaries, API key included | environment variables, no default for the secret |
| Logs | files in a `logs` folder under the application | JSON lines on standard output |
| Documents | files under the application folder | files in a mounted volume |
| List of documents | in memory | next to the documents, in the volume |
| Time zone and culture | whatever Windows provides | named explicitly, data in the image |
| Stopping | the service is killed | SIGTERM, requests in flight finish |
| Health | none | liveness and readiness endpoints |

## Layout

```
before/src/Inbox/     the service as it runs on the Windows server
attempt/Dockerfile    the first attempt: the old service, unchanged, in an image
after/src/Inbox/      the service changed to run as a container
after/Dockerfile      its image
tests/Inbox.Tests/    Scenarios.cs (10, over HTTP) and ContainerTests.cs (9, drive Docker)
```

## Run it

You need the .NET 10 SDK and Docker. From this folder:

```bash
docker build -f after/Dockerfile -t inbox:after .
```

```bash
docker run -d --read-only --tmpfs /tmp --cap-drop ALL -e ApiKey=demo-key -e Clock__FixedUtc=2026-07-01T22:30:00Z -p 127.0.0.1:5090:8080 inbox:after
```

```bash
dotnet test tests/Inbox.Tests
```

The scenarios expect the service at `http://localhost:5090` (`INBOX_URL`) with the clock fixed as above. The container
tests start their own containers from `inbox:after` (`INBOX_IMAGE`).

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 10 scenarios against the old service as a process | CI, Windows | 10 / 10 |
| 10 scenarios against the container | CI, Linux, Docker | 10 / 10 |
| 9 container tests against the image | CI, Linux, Docker | 9 / 9 |
| The same 19 tests against the first attempt | CI, Linux, Docker | fail as described below |

Not verified:

- **A real platform.** The tests drive Docker on one machine. Kubernetes, a cloud container service, their probes,
  rolling updates and autoscaling are not involved. The restrictions and signals used are the ones those platforms
  apply.
- **A real shared store.** Two copies share a Docker volume on one host. In a cloud, the equivalent is a network file
  share or, better, object storage and a database. The file-based store here shows the principle (no state in the
  container), not a recommendation for scale.
- **Load.** Twenty parallel uploads to two copies is a correctness check, not a performance test.
- **Windows containers.** The target is a Linux container. Applications that cannot leave Windows (COM, GDI+,
  Windows authentication) have a different path.

## The first attempt

The usual first step is a Dockerfile around the application as it is, on the smallest base image:
[`attempt/Dockerfile`](attempt/Dockerfile). The image builds without a warning. Measured against the tests:

| Image | Result |
|---|---|
| Old service on the alpine image | does not start: `CultureNotFoundException: Only the invariant culture is supported in globalization-invariant mode` |
| Old service on the default image | 8 of 10 scenarios pass; all 9 container tests fail |

The alpine image has no culture data and no time zone database. The service asks for the German culture when it
starts and stops there. On the default image it starts, and two scenarios fail on the report:

```csharp
// Works on Windows: file names are not case-sensitive and the backslash separates folders.
var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, @"templates\dailyreport.txt"));
```

The file on disk is `Templates/DailyReport.txt`. On Linux the backslash is an ordinary character in a file name.

The nine container tests fail for nine reasons. Run with the restrictions a careful platform applies, the service
does not start at all (`IOException: Read-only file system : '/app/logs'`). Without the restrictions:

| Container test | Old service in a container |
|---|---|
| Starts read-only, without privileges, reports ready | writes its logs into its own folder; has no readiness endpoint |
| Does not run as root | the image has no user set |
| Health endpoints | none |
| Not ready when storage is not writable | no way to say so |
| Documents survive replacement of the container | the list of documents is in memory |
| Two copies serve the same documents | each copy has its own list and its own numbering |
| Upload in progress finishes on stop | `docker stop` waits the full 20 s and kills it; the upload is lost |
| Logs are JSON lines on standard output | the application's own log goes to a file inside the container |
| Does not start without its secret | starts with the key from `appsettings.json` |

The stop test deserves a line. The Dockerfile ends with `ENTRYPOINT dotnet Inbox.dll`. In this form Docker starts a
shell, the shell starts the application, and the shell does not pass SIGTERM on. The application never learns that
it should stop.

## What changed

All in [`after/src/Inbox/Program.cs`](after/src/Inbox/Program.cs) and [`after/Dockerfile`](after/Dockerfile).

- **Image.** `aspnet:10.0-noble-chiseled-extra`: no shell, no package manager, a non-root user, and, unlike alpine
  and the plain chiseled image, the culture data and the time zone database. The time zone is named by its IANA
  identifier (`Europe/Berlin`) in configuration.
- **Paths.** `Path.Combine(AppContext.BaseDirectory, "Templates", "DailyReport.txt")`, spelled as on disk.
- **State.** Documents and their descriptions are files in `/data`, a volume. Nothing is kept in memory. A document
  number is claimed by creating its description file with `FileMode.CreateNew`, which fails if another copy was
  faster. That is what lets twenty uploads arrive at two copies at once and receive twenty different numbers.
- **Logs.** `AddJsonConsole`. The container writes no files of its own, which is what makes `--read-only` possible.
- **Configuration.** Everything from the environment. The API key has no default; without it the service logs one
  line and exits with code 1 instead of running with a key everyone knows.
- **Stopping.** The exec form `ENTRYPOINT ["dotnet", "Inbox.dll"]` makes the application process 1, so it receives
  SIGTERM. ASP.NET Core then stops accepting requests and lets the running ones finish.
- **Health.** `/healthz/live` says the process answers. `/healthz/ready` says it can do its job, which here means
  the volume is writable. With the volume mounted read-only the service is live and not ready.

In CI: with an upload in progress, `docker stop` returned after 3.1 s for the new image, the upload answered 201 and the
document was there afterwards. For the first attempt it returned after 20.2 s, the whole grace period.

## Two things the numbers do not flatter

- **The image got bigger, not smaller.** As `docker images` reports them in CI: first attempt on alpine 179 MB, final image 239 MB,
first attempt on the default image 365 MB. The culture and time zone data that alpine leaves out are the
  difference. A small image that cannot format a date is not a saving.
- **The first attempt looked fine.** It built, and on the default image it answered most requests. Everything that
  was wrong with it shows only when the container is restricted, stopped, replaced or doubled, which is to say in
  production and not on a developer's machine.
