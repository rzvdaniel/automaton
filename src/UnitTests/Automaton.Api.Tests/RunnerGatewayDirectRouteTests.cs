using System.Diagnostics;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using static Automaton.Api.Tests.BackendRouteConfigTestFixture;

namespace Automaton.Api.Tests;

[TestFixture]
public class RunnerGatewayDirectRouteTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef";

    [TestCase("http://172.18.0.1:23456", true)]
    [TestCase("http://10.0.0.5:20001", true)]
    [TestCase("http://192.168.1.2:30000", true)]
    [TestCase("http://127.0.0.1:23456", false)]
    [TestCase("http://8.8.8.8:23456", false)]
    [TestCase("http://172.32.0.1:23456", false)]
    [TestCase("http://runner.local:23456", false)]
    [TestCase("https://172.18.0.1:23456", false)]
    [TestCase("http://172.18.0.1:23456/x", false)]
    [TestCase("http://172.18.0.1", false)]
    [TestCase("http://172.18.0.1:80", false)]
    [TestCase("http://u:p@172.18.0.1:23456", false)]
    public void OnlyAPrivateIpAddressWithAPortIsAccepted(string url, bool accepted)
        => Assert.That(RunnerGatewayRegistry.TryParse(url, Secret, out _), Is.EqualTo(accepted));

    [TestCase(null)]
    [TestCase("short")]
    [TestCase("has space in the secret 0123456789")]
    public void ASecretThatIsMissingOrWeirdIsRefused(string? secret)
        => Assert.That(RunnerGatewayRegistry.TryParse("http://172.18.0.1:23456", secret, out _), Is.False);

    [Test]
    public async Task OnlyAReachableGatewayIsRemembered_AndChangesAreReported()
    {
        var reachable = false;
        var registry = new RunnerGatewayRegistry((_, _) => Task.FromResult(reachable));
        var runner = Guid.NewGuid();

        Assert.That(await registry.ReportAsync(runner, "http://172.18.0.1:23456", Secret, default), Is.False);
        Assert.That(registry.Find(runner), Is.Null);

        reachable = true;
        Assert.That(await registry.ReportAsync(runner, "http://172.18.0.1:23456", Secret, default), Is.True);
        Assert.That(await registry.ReportAsync(runner, "http://172.18.0.1:23456", Secret, default), Is.False);
        Assert.That(registry.Find(runner)!.Url.Port, Is.EqualTo(23456));

        reachable = false;
        Assert.That(await registry.ReportAsync(runner, "http://172.18.0.1:23456", Secret, default), Is.True);
        Assert.That(registry.Find(runner), Is.Null);
    }

    [Test]
    public async Task ARunnerWithAGatewayGetsDirectRoutes_OthersAndPausedAppsKeepTheTunnel()
    {
        var root = CreateTempDirectory();
        await using var db = CreateDbContext();
        var directRunner = Guid.NewGuid();
        var otherRunner = Guid.NewGuid();
        AddMachineRunner(db, directRunner);
        AddMachineRunner(db, otherRunner);
        var directApp = AddRoutedApp(db, directRunner, paused: false);
        var pausedApp = AddRoutedApp(db, directRunner, paused: true);
        var tunnelApp = AddRoutedApp(db, otherRunner, paused: false);
        await db.SaveChangesAsync();

        var registry = new RunnerGatewayRegistry((_, _) => Task.FromResult(true));
        await registry.ReportAsync(directRunner, "http://172.18.0.1:23456", Secret, default);
        await CreateService(db, root, gateways: registry).RefreshAsync();
        var content = await File.ReadAllTextAsync(Path.Combine(root, "managed-runtime-routes.yml"));

        var directTarget = db.AppDeploymentTargets.Single(t => t.WebAppId == directApp);
        var gateway = $"gateway-{directRunner:N}";
        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("  services:"));
            Assert.That(content, Does.Contain($"    {gateway}:"));
            Assert.That(content, Does.Contain("- url: \"http://172.18.0.1:23456\""));
            Assert.That(content, Does.Contain($"{RunnerGatewayRegistry.SecretHeader}: \"{Secret}\""));
            Assert.That(content, Does.Contain($"replacement: \"/apps/{directApp}/targets/{directTarget.Id}/$1\""));
            Assert.That(content, Does.Not.Contain($"/runtime-tunnel/apps/{directApp}/"));
            Assert.That(content, Does.Contain($"/runtime-tunnel/apps/{pausedApp}/"));
            Assert.That(content, Does.Contain($"/runtime-tunnel/apps/{tunnelApp}/"));
            // Private apps keep their sign-in check on the direct route.
            Assert.That(content, Does.Contain($"private-{directApp:N}-auth:"));
            Assert.That(content, Does.Contain($"\"private-{directApp:N}-rewrite\", \"{gateway}-secret\"]"));
        });
    }

    [Test]
    public async Task WithoutAnyGatewayTheRoutesAreUnchanged()
    {
        var root = CreateTempDirectory();
        await using var db = CreateDbContext();
        var runner = Guid.NewGuid();
        AddMachineRunner(db, runner);
        var app = AddRoutedApp(db, runner, paused: false);
        await db.SaveChangesAsync();

        await CreateService(db, root).RefreshAsync();
        var content = await File.ReadAllTextAsync(Path.Combine(root, "managed-runtime-routes.yml"));
        Assert.That(content, Does.Not.Contain("  services:"));
        Assert.That(content, Does.Contain($"/runtime-tunnel/apps/{app}/"));
    }

    [Test]
    public async Task TheSharedGatewayShellIsValidAndPublishesOnlyOnThePrivateAddress()
    {
        var script = "docker() { echo ''; }\nruntime_host_address=172.18.0.1\n" + RunnerGatewayShell.Setup + "\nprintf '%s' \"$gateway_args\"\n";
        var output = await RunShAsync(script);
        Assert.That(output, Does.Match(@"^-p 172\.18\.0\.1:\d{5}:8080 -e AUTOMATON_GATEWAY_SECRET=[0-9a-f]{64} -e AUTOMATON_RUNTIME_GATEWAY_URL=http://172\.18\.0\.1:\d{5}$"));
        Assert.That(await RunShAsync("docker() { echo ''; }\n" + RunnerGatewayShell.Setup + "\nprintf '[%s]' \"$gateway_args\""), Is.EqualTo("[]"));

        var kept = await RunShAsync("docker() { echo ''; }\nruntime_host_address=10.1.1.1 gateway_port=23456 gateway_secret=abc123\n" + RunnerGatewayShell.Setup + "\nprintf '%s' \"$gateway_args\"");
        Assert.That(kept, Is.EqualTo("-p 10.1.1.1:23456:8080 -e AUTOMATON_GATEWAY_SECRET=abc123 -e AUTOMATON_RUNTIME_GATEWAY_URL=http://10.1.1.1:23456"));
    }

    private static Guid AddRoutedApp(Automaton.Api.Data.ApplicationDbContext db, Guid runnerId, bool paused)
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        AddApp(db, appId, target, GenericRuntimeContractJson());
        var app = db.WebApps.Local.Single(a => a.Id == appId);
        app.RequiresAuthentication = true;
        app.IsPaused = paused;
        return appId;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-gw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> RunShAsync(string script)
    {
        using var process = Process.Start(new ProcessStartInfo("/bin/sh", ["-eu", "-c", script]) { RedirectStandardOutput = true })!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.EqualTo(0));
        return output;
    }
}
