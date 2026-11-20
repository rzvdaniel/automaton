using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CatalogInstallCommandTests
{
    private static readonly JsonElement Application = JsonDocument.Parse("""
        {"settings":[{"name":"SITE","required":true},{"name":"MODE","default":"fast"}],
         "secrets":[{"name":"TOKEN"},{"name":"SHARED","from":"project:SHARED"}],
         "permissions":["network"]}
        """).RootElement;

    [Test]
    public void DeploymentFailure_ReadsAFinishedOperationsResultWhichSaysSuccessFalse()
    {
        // `--wait` gets the operation's result, not its status: a failed build is success:false with the reason in "message".
        var failed = JsonDocument.Parse("""{"success":false,"state":"ready","message":"Application build failed: No space left on device"}""").RootElement;
        var ok = JsonDocument.Parse("""{"success":true,"state":"ready","message":null}""").RootElement;

        Assert.That(CatalogInstallCommand.DeploymentFailure(failed), Is.EqualTo("The deployment failed: Application build failed: No space left on device"));
        Assert.That(CatalogInstallCommand.DeploymentFailure(ok), Is.Null);
    }

    [Test]
    public void Install_TakesNoOptionThatCarriesASecretValue()
    {
        var install = CatalogInstallCommand.BuildInstall(new ConfigService());
        Assert.That(install.Options.Select(o => o.Name), Is.EquivalentTo(
            new[] { "source", "name", "runner", "setting", "secrets-file", "secrets-stdin", "wait", "no-schedules", "schedule", "json" }));
        Assert.That(install.Description, Does.Contain("--secrets-file").And.Contain("hidden prompt"));
    }

    [Test]
    public void List_FiltersByTextCategorySourceAndInstalled() =>
        Assert.That(CatalogInstallCommand.BuildList(new ConfigService()).Options.Select(o => o.Name),
            Is.EquivalentTo(new[] { "search", "category", "source", "installed", "refresh", "json" }));

    [Test]
    public void ParseLines_SkipsCommentsAndBlanks_KeepsEverythingAfterTheFirstEquals_AndHandlesCrlf()
    {
        var values = CatalogInstallValues.ParseLines("# note\r\n\r\nTOKEN=a=b c#d\r\nOTHER=\r\n", "file");

        Assert.That(values, Is.EqualTo(new Dictionary<string, string> { ["TOKEN"] = "a=b c#d", ["OTHER"] = "" }));
    }

    [TestCase("noequals")]
    [TestCase("=value")]
    [TestCase("BAD NAME=1")]
    public void ParseLines_RejectsALineWithoutAValidName_NamingTheLineButNotTheValue(string line)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => CatalogInstallValues.ParseLines("OK=1\n" + line, "the file"))!;

        Assert.That(failure.Message, Does.Contain("the file, line 2"));
    }

    [Test]
    public void Secrets_ComeFromAFileOrStandardInput_NeverBoth()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "TOKEN=from-file\n");
            Assert.That(CatalogInstallValues.ReadSecrets(path, false)["TOKEN"], Is.EqualTo("from-file"));
            Assert.That(CatalogInstallValues.ReadSecrets(null, true, new StringReader("TOKEN=from-stdin\n"))["TOKEN"], Is.EqualTo("from-stdin"));
            Assert.That(CatalogInstallValues.ReadSecrets(null, false), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => CatalogInstallValues.ReadSecrets(path, true));
            Assert.Throws<InvalidOperationException>(() => CatalogInstallValues.ReadSecrets(path + ".missing", false));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Check_DoesNotReportAnOptionalSecret()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{"secrets":[{"name":"HOOK","optional":true},{"name":"TOKEN"}]}""");

        var (_, _, missingSecrets) = CatalogInstallValues.Check(document.RootElement, new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.That(missingSecrets, Is.EqualTo(new[] { "TOKEN" }));
    }

    [Test]
    public void Check_ReportsMissingRequiredSettingsAndSecrets_ButNotDefaultsOrVaultSecrets()
    {
        var (unknown, missingSettings, missingSecrets) = CatalogInstallValues.Check(Application, new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(unknown, Is.Empty);
            Assert.That(missingSettings, Is.EqualTo(new[] { "SITE" }));
            Assert.That(missingSecrets, Is.EqualTo(new[] { "TOKEN" }));
        });
    }

    [Test]
    public void Check_AcceptsEverythingGivenAndFlagsNamesTheAppDoesNotDeclare()
    {
        var (unknown, missingSettings, missingSecrets) = CatalogInstallValues.Check(Application,
            new Dictionary<string, string> { ["SITE"] = "x", ["EXTRA"] = "1" }, new Dictionary<string, string> { ["TOKEN"] = "t", ["NOPE"] = "z" });

        Assert.Multiple(() =>
        {
            Assert.That(unknown, Is.EquivalentTo(new[] { "setting EXTRA", "secret NOPE" }));
            Assert.That(missingSettings, Is.Empty);
            Assert.That(missingSecrets, Is.Empty);
        });
    }

    [Test]
    public void ListOutput_ShowsTheIdAndSourceThatInstallNeeds()
    {
        var entry = JsonDocument.Parse("""{"id":"flow-check","sourceId":"official","name":"Flow check","category":"tools","permissions":["network"],"installedAppId":null}""").RootElement;
        var writer = new StringWriter();
        var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings { Out = new Spectre.Console.AnsiConsoleOutput(writer), Ansi = Spectre.Console.AnsiSupport.No, ColorSystem = Spectre.Console.ColorSystemSupport.NoColors });
        console.Profile.Width = 120;

        CatalogInstallCommand.RenderList([entry], console);

        Assert.That(writer.ToString(), Does.Contain("flow-check").And.Contain("official").And.Contain("network").And.Contain("automaton catalog install"));
    }

    [Test]
    public void EmptyValuesCountAsNotGiven_SoTheyAreCaughtBeforeAnythingIsCreated()
    {
        var secrets = CatalogInstallValues.WithoutEmpty(CatalogInstallValues.ParseLines("TOKEN=\nSITE=  \nKEEP=x", "file"));

        var (_, missingSettings, missingSecrets) = CatalogInstallValues.Check(Application, secrets, secrets);

        Assert.Multiple(() =>
        {
            Assert.That(secrets.Keys, Is.EqualTo(new[] { "KEEP" }));
            Assert.That(missingSettings, Is.EqualTo(new[] { "SITE" }));
            Assert.That(missingSecrets, Is.EqualTo(new[] { "TOKEN" }));
        });
    }

    [Test]
    public void RunnerHelp_DoesNotPromiseAnUnconditionalPick() =>
        Assert.That(CatalogInstallCommand.BuildInstall(new ConfigService()).Options.Single(o => o.Name == "runner").Description,
            Does.Contain("first online Runner").And.Contain("refused"));

    [TestCase("""{"id":"a","executionRunnerId":null}""", true)]
    [TestCase("""{"id":"a","executionRunnerId":""}""", true)]
    [TestCase("""{"id":"a","executionRunnerId":"7d0f2f55-6b0e-4c3a-9a55-1d1f4f0c9b11"}""", false)]
    [TestCase("""{"id":"a"}""", false)]
    public void NoRunner_FlagsOnlyAnAppAnsweredWithoutARunner(string json, bool flagged) =>
        Assert.That(CatalogInstallCommand.NoRunner(JsonDocument.Parse(json).RootElement) is not null, Is.EqualTo(flagged));

    private static readonly JsonElement WithSchedules = JsonDocument.Parse("""
        {"schedules":[{"name":"Stall check","action":"stall","cron":"*/5 * * * *"},{"name":"Morning report","action":"report","intervalSeconds":3600}]}
        """).RootElement;

    [Test]
    public void Schedules_AreAllKeptByDefault() =>
        Assert.That(CatalogSchedules.Select(WithSchedules, false, []).Select(CatalogSchedules.Name), Is.EqualTo(new[] { "Stall check", "Morning report" }));

    [Test]
    public void Schedules_NoSchedulesKeepsNone_AndAnAppWithoutAnyKeepsNone()
    {
        Assert.That(CatalogSchedules.Select(WithSchedules, true, []), Is.Empty);
        Assert.That(CatalogSchedules.Select(Application, false, []), Is.Empty);
    }

    [Test]
    public void Schedules_ScheduleKeepsOnlyTheNamedOnes_IgnoringCase()
        => Assert.That(CatalogSchedules.Select(WithSchedules, false, ["morning REPORT"]).Select(CatalogSchedules.Name), Is.EqualTo(new[] { "Morning report" }));

    [Test]
    public void Schedules_AnUnknownNameOrBothOptionsAreRefusedBeforeAnythingChanges()
    {
        Assert.That(() => CatalogSchedules.Select(WithSchedules, false, ["Lessons"]), Throws.InvalidOperationException.With.Message.Contain("Lessons").And.Message.Contain("Stall check"));
        Assert.That(() => CatalogSchedules.Select(WithSchedules, true, ["Stall check"]), Throws.InvalidOperationException);
    }

    [Test]
    public void Update_OffersTheSameScheduleOptions() =>
        Assert.That(CatalogUpdateCommand.Build(new ConfigService()).Options.Select(o => o.Name), Does.Contain("no-schedules").And.Contain("schedule"));

    [Test]
    public async Task CreateSchedules_SkipsOnesTheAppHasAndPostsStudiosBody()
    {
        const string appId = "11111111-1111-1111-1111-111111111111";
        var posted = new List<JsonElement>();
        using var http = new HttpClient(new Stub(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json("""[{"applicationId":"11111111-1111-1111-1111-111111111111","name":"My app: Stall check"},{"applicationId":"22222222-2222-2222-2222-222222222222","name":"My app: Morning report"}]""");
            posted.Add(JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement.Clone());
            return Json("{}");
        })) { BaseAddress = new Uri("https://api.example.com/") };
        using var client = new AutomatonApiClient(http, "test-key");

        var (created, existing) = await CatalogSchedules.CreateAsync(client, appId, "My app", CatalogSchedules.Select(WithSchedules, false, []));

        Assert.That(created, Is.EqualTo(new[] { "Morning report" }));
        Assert.That(existing, Is.EqualTo(new[] { "Stall check" }));
        Assert.That(posted, Has.Count.EqualTo(1));
        var body = posted[0];
        Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("My app: Morning report"));
        Assert.That(body.GetProperty("applicationId").GetString(), Is.EqualTo(appId));
        Assert.That(body.GetProperty("actionKey").GetString(), Is.EqualTo("report"));
        Assert.That(body.GetProperty("scheduleType").GetString(), Is.EqualTo("Interval"));
        Assert.That(body.GetProperty("intervalSeconds").GetInt32(), Is.EqualTo(3600));
        Assert.That(body.GetProperty("timeZone").GetString(), Is.EqualTo("Etc/UTC"));
        Assert.That(body.GetProperty("enabled").GetBoolean(), Is.True);
    }

    private static HttpResponseMessage Json(string body) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
