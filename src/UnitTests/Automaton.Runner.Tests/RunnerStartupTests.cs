using System.Net;
using System.Net.Http.Json;
using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerStartupTests
{
    [Test]
    public async Task ManagedServiceHost_StartsBeforeRunnerServiceMode()
    {
        using var environment = TestEnvironment.Managed();
        using var host = Program.CreateHostBuilder([]).Build();

        var startup = Task.Run(() => host.StartAsync());
        Assert.That(
            await Task.WhenAny(startup, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(startup),
            "Managed Runner host startup must not block on hosted-service resolution.");
        await startup;

        Assert.That(host.Services.GetRequiredService<RunnerHostService>(), Is.Not.Null);
        Assert.That(
            host.Services.GetServices<IHostedService>(),
            Has.Some.TypeOf<RunnerRuntimeContainerHostedService>());
        await host.StopAsync();
    }

    [Test]
    public async Task ManagedEnrollment_UsesExchangeBoundaryAfterHostBuild()
    {
        using var environment = TestEnvironment.Managed();
        var handler = new EnrollmentHandler();
        using var host = Program.CreateHostBuilder([])
            .ConfigureServices(services => services.AddSingleton(handler))
            .Build();
        await host.StartAsync();
        using var enrollmentClient = new HttpClient(handler);
        var runner = ManagedRunnerEnrollmentFixture.CreateRunner(host, enrollmentClient);

        Assert.That(await runner.EnrollAsync(), Is.True);
        Assert.That(handler.RequestUri?.AbsolutePath, Is.EqualTo("/api/runner-enrollments/exchange"));
        Assert.That(handler.RequestMethod, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.RequestBody?.EnrollmentToken, Is.EqualTo("re_test-token"));
        await host.StopAsync();
    }

    [Test]
    public async Task MachineManagementProfileReload_RestoresPersistedRunnerIdentityAfterRestart()
    {
        using var environment = TestEnvironment.Managed();
        var storage = new FileStorageService();
        var runnerId = Guid.NewGuid();
        await storage.SaveRunnerConfigAsync(runnerId.ToString(), "machine-runner");
        await storage.SaveInstallationProfileAsync(new RunnerInstallationProfile
        {
            Purpose = "Managed",
            ApplicationProfile = "MachineManagement",
            Capabilities = ["MachineManagement"],
            InstallationId = "ri_test"
        });

        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", null);
        var workflow = new RunnerEnrollmentWorkflow(null!, storage, null!, new HttpClient(), Serilog.Log.Logger);

        await workflow.ApplyInstallationProfileAsync();

        Assert.That(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID"), Is.EqualTo(runnerId.ToString()));
        Assert.That(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"), Is.EqualTo("MachineManagement"));
    }

    [Test]
    public async Task FastApiReadinessWait_StopsWhenServiceIsStopping()
    {
        using var host = Program.CreateHostBuilder([]).Build();
        var runner = host.Services.GetRequiredService<RunnerHostService>();
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.WaitForFastApiAsync(maxWaitSeconds: 30, cancellationToken: stopping.Token));
    }

    private sealed class EnrollmentHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public RunnerEnrollmentExchangeRequest? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestMethod = request.Method;
            RequestBody = await request.Content!.ReadFromJsonAsync<RunnerEnrollmentExchangeRequest>(cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RunnerEnrollmentExchangeResponse
                {
                    RunnerId = Guid.NewGuid(),
                    RunnerHostId = Guid.NewGuid(),
                    RunnerName = "managed-test-runner",
                    ApiKey = "ak_test-enrolled-credential",
                    Purpose = "Managed",
                    Capabilities = ["Standard"],
                    InstallationId = "ri_test"
                })
            };
        }
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> values;
        private readonly string previousDirectory;

        private TestEnvironment(Dictionary<string, string?> values, string previousDirectory) => (this.values, this.previousDirectory) = (values, previousDirectory);

        public static TestEnvironment Managed()
        {
            var names = new[]
            {
                "AUTOMATON_SERVER_URL", "AUTOMATON_ENROLLMENT_TOKEN", "AUTOMATON_RUNNER_PURPOSE",
                "AUTOMATON_HOSTED_RUNNER_ID", "AUTOMATON_RUNNER_APPLICATION_PROFILE", "AUTOMATON_RUNTIME_HTTP_PORT",
                "AUTOMATON_RUNTIME_HOST_ADDRESS", "AUTOMATON_WORKSPACE_ROOT", "USERPROFILE", "AUTOMATON_API_KEY",
                "AUTOMATON_RUNNER_ID", "AUTOMATON_RUNNER_NAME", "APPDATA"
            };
            var values = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            Environment.SetEnvironmentVariable("AUTOMATON_SERVER_URL", "http://127.0.0.1:1");
            Environment.SetEnvironmentVariable("AUTOMATON_ENROLLMENT_TOKEN", "re_test-token");
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Managed");
            Environment.SetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID", Guid.NewGuid().ToString());
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", "Standard");
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", GetFreePort().ToString());
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HOST_ADDRESS", "127.0.0.1");
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", Path.Combine(Path.GetTempPath(), "automaton-runner-tests", Guid.NewGuid().ToString("N")));
            var profileRoot = Path.Combine(Path.GetTempPath(), "automaton-runner-tests", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("USERPROFILE", profileRoot);
            Environment.SetEnvironmentVariable("APPDATA", Path.Combine(profileRoot, "AppData", "Roaming"));
            Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "");
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", null);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_NAME", null);
            var directory = Path.Combine(Path.GetTempPath(), "automaton-runner-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var previousDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = directory;
            return new(values, previousDirectory);
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = previousDirectory;
            foreach (var (name, value) in values)
                Environment.SetEnvironmentVariable(name, value);
        }

        private static int GetFreePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
