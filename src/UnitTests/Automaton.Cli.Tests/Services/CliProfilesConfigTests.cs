using Automaton.Cli;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
[NonParallelizable]
public class CliProfilesConfigTests
{
    private string root = null!;
    private ConfigService config = null!;
    private string? originalProfile;
    private string? originalServer;
    private string? originalKey;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "automaton-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        config = new ConfigService(Path.Combine(root, "config.json"));
        originalProfile = Environment.GetEnvironmentVariable("AUTOMATON_PROFILE");
        originalServer = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        originalKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        Environment.SetEnvironmentVariable("AUTOMATON_PROFILE", null);
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", null);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_PROFILE", originalProfile);
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", originalServer);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", originalKey);
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public void LoadingV1MigratesToVersion2WithoutChangingKeyOrPermissions()
    {
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, "{\"server\":\"https://api.example.test\",\"apiKey\":\"ak_one\"}");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var migrated = config.LoadProfilesConfig();

        Assert.That(migrated.Version, Is.EqualTo(2));
        Assert.That(migrated.Current, Is.EqualTo("example.test"));
        Assert.That(migrated.Profiles["example.test"].ApiKey, Is.EqualTo("ak_one"));
        Assert.That(File.ReadAllText(path), Does.Contain("\"version\": 2"));
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    public void ExplicitEnvironmentAndAppServerSelectMatchingCredentialPair()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        config.SaveProfile("two", new GlobalConfig { Server = "https://api.two.test", ApiKey = "ak_two" });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig { Server = "https://api.two.test" });

        var matched = config.ResolveContext(directory: app);
        Assert.That((matched.ProfileName, matched.Server, matched.ApiKey),
            Is.EqualTo(("two", "https://api.two.test", "ak_two")));

        config.ProfileOverride = "two";
        var explicitSelected = config.ResolveContext(directory: app);
        Assert.That(explicitSelected.ProfileName, Is.EqualTo("two"));

        config.ProfileOverride = null;
        Environment.SetEnvironmentVariable("AUTOMATON_PROFILE", "one");
        var error = Assert.Throws<InvalidOperationException>(() => config.ResolveContext(directory: app));
        Assert.That(error!.Message, Does.Contain("This app is on https://api.two.test"));
    }

    [Test]
    public void ExplicitProfileOnAnotherServerThanLinkedAppIsRejected()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig
        {
            Server = "https://api.two.test", AppId = "app-on-two"
        });
        config.ProfileOverride = "one";

        var error = Assert.Throws<InvalidOperationException>(() => config.ResolveContext(directory: app));
        Assert.That(error!.Message, Does.Contain("This app is on https://api.two.test"));
    }

    [Test]
    public void ExplicitProfileWithEnvironmentCredentialsIsAnError()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        config.ProfileOverride = "one";
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "ak_env");
        Assert.Throws<InvalidOperationException>(() => config.ResolveContext(directory: root));
    }

    [Test]
    public void ProjectLocalKeyWithoutServerIsRejected()
    {
        config.SaveProfile("two", new GlobalConfig { Server = "https://api.two.test", ApiKey = "ak_two" });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig { ApiKey = "ak_local" });
        var error = Assert.Throws<InvalidOperationException>(() => config.ResolveContext(directory: app));
        Assert.That(error!.Message, Does.Contain("no server"));
    }

    [Test]
    public void MalformedLegacyServerReportsClearErrorAndKeepsFile()
    {
        var path = Path.Combine(root, "config.json");
        var original = "{\"server\":\"localhost:7091\",\"apiKey\":\"ak_one\"}";
        File.WriteAllText(path, original);
        var error = Assert.Throws<InvalidOperationException>(() => config.LoadProfilesConfig());
        Assert.That(error!.Message, Does.Contain("not a valid URL"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original));
    }

    [Test]
    public void LogoutInsideAppRemovesMatchingProfile()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        config.SaveProfile("two", new GlobalConfig { Server = "https://api.two.test", ApiKey = "ak_two" });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig { Server = "https://api.two.test" });

        Assert.That(config.RemoveSelectedProfile(app), Is.True);

        var profiles = config.LoadProfilesConfig();
        Assert.That(profiles.Profiles.Keys, Is.EquivalentTo(new[] { "one" }));
        Assert.That(profiles.Current, Is.EqualTo("one"));
    }

    [Test]
    public void ClearingPendingAuthorizationUpdatesMatchedProfileOnly()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        config.SaveProfile("two", new GlobalConfig
        {
            Server = "https://api.two.test", ApiKey = "ak_two",
            PendingAuthorizationCompletion = new PendingCliAuthorizationCompletion
            {
                RequestId = Guid.NewGuid(), Server = "https://api.two.test", Verifier = "pending"
            }
        });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig { Server = "https://api.two.test" });
        var resolved = config.ResolveContext(directory: app);

        config.ClearPendingAuthorizationCompletion(app, resolved);

        var profiles = config.LoadProfilesConfig();
        Assert.That(profiles.Profiles["one"].ApiKey, Is.EqualTo("ak_one"));
        Assert.That(profiles.Profiles["two"].ApiKey, Is.EqualTo("ak_two"));
        Assert.That(profiles.Profiles["two"].PendingAuthorizationCompletion, Is.Null);
    }

    [Test]
    public void AppWithoutMatchingProfileFailsInsteadOfUsingCurrentKey()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig { Server = "https://api.two.test" });

        var error = Assert.Throws<InvalidOperationException>(() => config.ResolveContext(directory: app));
        Assert.That(error!.Message, Does.Contain("This app is on https://api.two.test"));
    }

    [Test]
    public void ExistingProjectLocalCredentialForLinkedServerStaysUsable()
    {
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig
        {
            Server = "https://api.two.test", ApiKey = "ak_local"
        });

        var resolved = config.ResolveContext(directory: app);

        Assert.That(resolved.Server, Is.EqualTo("https://api.two.test"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_local"));
    }

    [Test]
    public void LogoutOfCurrentProfileRetainsOthersAndClearsCurrent()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        config.SaveProfile("two", new GlobalConfig { Server = "https://api.two.test", ApiKey = "ak_two" });

        Assert.That(config.RemoveSelectedProfile(), Is.True);

        var profiles = config.LoadProfilesConfig();
        Assert.That(profiles.Profiles.Keys, Is.EquivalentTo(new[] { "two" }));
        Assert.That(profiles.Current, Is.Null);
        Assert.That(config.ResolveContext(directory: root).ApiKey, Is.Null);
    }

    [Test]
    public async Task GlobalProfileOptionIsAcceptedByCommandParser()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        using var output = new StringWriter();
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            var code = await CliApplication.RunAsync(["--profile", "one", "profiles", "--json"], config);
            Assert.That(code, Is.Zero);
            Assert.That(config.ProfileOverride, Is.EqualTo("one"));
            Assert.That(output.ToString(), Does.Contain("keyFingerprint"));
            Assert.That(output.ToString(), Does.Not.Contain("ak_one"));
        }
        finally { Console.SetOut(previous); }
    }

    [Test]
    public async Task WhoAmIInUnmatchedAppReportsLoginInstruction()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "automaton.json"), "{}");
        config.SaveNamedEnvironmentConfig(app, "live", new GlobalConfig { Server = "https://api.two.test" });
        using var output = new StringWriter();
        var previousOutput = Console.Out;
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Console.SetOut(output);
            Directory.SetCurrentDirectory(app);
            await CliApplication.RunAsync(["whoami", "--json"], config);
            Assert.That(output.ToString(), Does.Contain("This app is on https://api.two.test"));
            Assert.That(output.ToString(), Does.Contain("automaton login --server https://api.two.test"));
            Assert.That(output.ToString(), Does.Not.Contain("ak_one"));
        }
        finally
        {
            Console.SetOut(previousOutput);
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.ExitCode = 0;
        }
    }

    [Test]
    public async Task LogoutProfileOptionRemovesOnlyThatProfile()
    {
        config.SaveProfile("one", new GlobalConfig { Server = "https://api.one.test", ApiKey = "ak_one" });
        config.SaveProfile("two", new GlobalConfig { Server = "https://api.two.test", ApiKey = "ak_two" });
        var code = await CliApplication.RunAsync(["logout", "--profile", "two", "--json"], config);
        Assert.That(code, Is.Zero);
        Assert.That(config.LoadProfilesConfig().Profiles.Keys, Is.EquivalentTo(new[] { "one" }));
    }

    [Test]
    public async Task LoginToSecondServerKeepsFirstProfileCredential()
    {
        await LoginWithFakeServerAsync("one", "ak_one");
        await LoginWithFakeServerAsync("two", "ak_two");

        var profiles = config.LoadProfilesConfig();
        Assert.That(profiles.Profiles.Keys, Is.EquivalentTo(new[] { "one", "two" }));
        Assert.That(profiles.Profiles["one"].ApiKey, Is.EqualTo("ak_one"));
        Assert.That(profiles.Profiles["two"].ApiKey, Is.EqualTo("ak_two"));
        Assert.That(profiles.Current, Is.EqualTo("one"));
    }

    private async Task LoginWithFakeServerAsync(string profile, string key)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var responseTask = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var body = "{\"name\":\"Test User\"}";
            var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header + body));
        });
        var code = await CliApplication.RunAsync(["login", "--server", $"http://127.0.0.1:{port}",
            "--profile", profile, "--key", key, "--json"], config);
        await responseTask;
        Assert.That(code, Is.Zero);
    }
}
