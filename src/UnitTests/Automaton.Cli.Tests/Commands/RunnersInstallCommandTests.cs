using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Net;
using System.Text;
using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class RunnersInstallCommandTests
{
    private const string Secret = "enrollment-secret";
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(5);

    [TearDown]
    public void ResetHooks()
    {
        RunnersInstallCommand.IsNonInteractive = () => Console.IsInputRedirected || Console.IsOutputRedirected;
    }

    [Test]
    public void ToShellInput_UsesOnlyLineFeedsSoWindowsCannotBreakTheHereDocument()
    {
        var script = "/bin/sh <<'AUTOMATON_RUNNER_INSTALL'\r\necho ok\r\nAUTOMATON_RUNNER_INSTALL\r\n";

        var input = RunnersInstallCommand.ToShellInput(script);

        Assert.That(input, Is.EqualTo("/bin/sh <<'AUTOMATON_RUNNER_INSTALL'\necho ok\nAUTOMATON_RUNNER_INSTALL\n"));
        Assert.That(input, Does.Not.Contain("\r"));
    }

    [Test]
    public void ToShellInput_EndsWithSingleLineFeed()
        => Assert.That(RunnersInstallCommand.ToShellInput("echo a\n\n"), Is.EqualTo("echo a\n"));

    [Test]
    public void SshInstallerReceivesSecretOnlyOnStdinAndRedactsFailure()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Requires a POSIX shell script as a fake ssh.");
        var directory = Path.Combine(Path.GetTempPath(), "automaton-cli-ssh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var script = Path.Combine(directory, "ssh");
            File.WriteAllText(script, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$TEST_SSH_ARGS\"\ncat > \"$TEST_SSH_STDIN\"\necho enrollment-secret >&2\nexit 7\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var argsPath = Path.Combine(directory, "args");
            var stdinPath = Path.Combine(directory, "stdin");
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + oldPath);
            Environment.SetEnvironmentVariable("TEST_SSH_ARGS", argsPath);
            Environment.SetEnvironmentVariable("TEST_SSH_STDIN", stdinPath);
            var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RunnersInstallCommand.ExecuteAsync("AUTOMATON_ENROLLMENT_TOKEN='enrollment-secret'", "user@host", 30, CancellationToken.None));
            var args = File.ReadAllText(argsPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Multiple(() =>
            {
                Assert.That(args, Is.EqualTo(new[] { "-T", "--", "user@host", "sudo bash -s" }));
                Assert.That(File.ReadAllText(stdinPath), Does.Contain(Secret));
                Assert.That(error!.Message, Does.Contain("Installer exited 7"));
                Assert.That(error.Message, Does.Not.Contain(Secret));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("TEST_SSH_ARGS", null);
            Environment.SetEnvironmentVariable("TEST_SSH_STDIN", null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(new[] { "--name", "test", "--local", "--json", "--yes", "--timeout", "0" }, "--timeout must be positive")]
    [TestCase(new[] { "--name", "test", "--local", "--ssh", "user@host", "--json", "--yes" }, "Choose one of --ssh, --local, or --print-command")]
    [TestCase(new[] { "--name", "test", "--json", "--yes" }, "Choose --ssh, --local, or --print-command")]
    [TestCase(new[] { "--name", "test", "--local", "--json" }, "Use --yes to acknowledge host access")]
    public async Task InvalidOrUnconfirmedInvocationFailsBeforeCreatingInstallation(string[] args, string expectedError)
    {
        RunnersInstallCommand.IsNonInteractive = () => true;
        var root = new RootCommand { RunnersCommand.Build(new ConfigService()) };
        var parser = new CommandLineBuilder(root).Build();
        using var output = new StringWriter();
        var previous = Console.Out;
        var previousError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(TextWriter.Null);
            var code = await parser.InvokeAsync(["runners", "install", .. args]);
            Assert.That(code, Is.EqualTo(1));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(json.RootElement.GetProperty("error").GetString(), Does.Contain(expectedError));
        }
        finally { Console.SetOut(previous); Console.SetError(previousError); }
    }

    [Test]
    public async Task InstallRequestsHostAccessAcknowledgementWaitsAndReturnsRunnerId()
    {
        var fake = new FakeApi();
        fake.Statuses.Enqueue("Pending");
        fake.Statuses.Enqueue("RunnerConnected");
        using var client = fake.CreateClient();
        string? executed = null;

        var id = await RunnersInstallCommand.InstallAsync(client, new RunnersInstallCommand.InstallRequest("main", null, null, false, 30, 30),
            (command, _, _, _) => { executed = command; return Task.CompletedTask; }, FastPoll, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo("runner-1"));
            Assert.That(executed, Does.Contain(Secret));
            using var body = JsonDocument.Parse(fake.InstallationBody!);
            Assert.That(body.RootElement.GetProperty("acknowledgeHostAccess").GetBoolean(), Is.True);
            Assert.That(body.RootElement.GetProperty("runnerName").GetString(), Is.EqualTo("main"));
            Assert.That(fake.Requests, Does.Not.Contain("POST /api/projects/p1/runners/runner-1"));
        });
    }

    [Test]
    public void WaitTimesOutWhenRunnerNeverConnects()
    {
        var fake = new FakeApi { RepeatStatus = "Pending" };
        using var client = fake.CreateClient();
        var error = Assert.ThrowsAsync<TimeoutException>(async () =>
            await RunnersInstallCommand.InstallAsync(client, new RunnersInstallCommand.InstallRequest("main", null, null, false, 1, 30),
                (_, _, _, _) => Task.CompletedTask, FastPoll, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("did not connect within 1 seconds"));
    }

    [Test]
    public async Task WaitRetriesTransientServerErrors()
    {
        var fake = new FakeApi();
        fake.FailStatusTimes = 2;
        fake.Statuses.Enqueue("RunnerConnected");
        using var client = fake.CreateClient();
        var id = await RunnersInstallCommand.WaitForRunnerAsync(client, "inst-1", 30, FastPoll, CancellationToken.None);
        Assert.That(id, Is.EqualTo("runner-1"));
    }

    [Test]
    public async Task ProjectAddsRunnerAfterConnection()
    {
        var fake = new FakeApi();
        fake.Statuses.Enqueue("RunnerConnected");
        using var client = fake.CreateClient();
        string? reported = null;

        var id = await RunnersInstallCommand.InstallAsync(client, new RunnersInstallCommand.InstallRequest("main", "Demo", null, false, 30, 30),
            (_, _, _, _) => Task.CompletedTask, FastPoll, CancellationToken.None, found => reported = found);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo("runner-1"));
            Assert.That(reported, Is.EqualTo("runner-1"));
            Assert.That(fake.Requests, Does.Contain("POST /api/projects/p1/runners/runner-1"));
        });
    }

    [Test]
    public void UnknownProjectFailsBeforeCreatingInstallation()
    {
        var fake = new FakeApi();
        using var client = fake.CreateClient();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnersInstallCommand.InstallAsync(client, new RunnersInstallCommand.InstallRequest("main", "Missing", null, false, 30, 30),
                (_, _, _, _) => Task.CompletedTask, FastPoll, CancellationToken.None));
        Assert.That(fake.Requests, Does.Not.Contain("POST /api/self-hosted-runners/installations"));
    }

    [Test]
    public async Task PrintCommandShowsSecretOnlyOnStdoutWithWarningOnStderr()
    {
        var fake = new FakeApi();
        fake.Statuses.Enqueue("RunnerConnected");
        using var client = fake.CreateClient();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            await RunnersInstallCommand.InstallAsync(client, new RunnersInstallCommand.InstallRequest("main", null, null, true, 30, 30),
                (_, _, _, _) => throw new AssertionException("Installer must not run with --print-command."), FastPoll, CancellationToken.None);
        }
        finally { Console.SetOut(previousOut); Console.SetError(previousErr); }

        Assert.Multiple(() =>
        {
            Assert.That(stdout.ToString(), Does.Contain(Secret));
            Assert.That(stderr.ToString(), Does.Not.Contain(Secret));
            Assert.That(stderr.ToString(), Does.Contain("One-time secret"));
        });
    }

    private sealed class FakeApi
    {
        public Queue<string> Statuses { get; } = new();
        public string? RepeatStatus { get; init; }
        public int FailStatusTimes { get; set; }
        public List<string> Requests { get; } = [];
        public string? InstallationBody { get; private set; }

        public AutomatonApiClient CreateClient()
            => new(new HttpClient(new Handler(this)) { BaseAddress = new Uri("https://api.example.com/") }, "test-key");

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            Requests.Add(key);
            switch (key)
            {
                case "POST /api/self-hosted-runners/installations":
                    InstallationBody = request.Content!.ReadAsStringAsync().Result;
                    return Json(new { installationId = "inst-1", installationCommand = $"AUTOMATON_ENROLLMENT_TOKEN='{Secret}' run", expiresAtUtc = "2030-01-01T00:00:00Z" });
                case "GET /api/self-hosted-runners/installations/inst-1/status":
                    if (FailStatusTimes > 0) { FailStatusTimes--; return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("bad") }; }
                    var status = Statuses.Count > 0 ? Statuses.Dequeue() : RepeatStatus ?? "Pending";
                    return Json(new { status, runnerId = status == "RunnerConnected" ? "runner-1" : null });
                case "GET /api/projects":
                    return Json(new[] { new { id = "p1", name = "Demo", displayName = "Demo" } });
                case "POST /api/projects/p1/runners/runner-1":
                    return Json(new { });
                default:
                    throw new AssertionException($"Unexpected request {key}");
            }
        }

        private static HttpResponseMessage Json(object value)
            => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

        private sealed class Handler(FakeApi owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(owner.Respond(request));
        }
    }
}
