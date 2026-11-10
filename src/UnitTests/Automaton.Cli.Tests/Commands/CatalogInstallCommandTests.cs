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
    public void Install_TakesNoOptionThatCarriesASecretValue()
    {
        var install = CatalogInstallCommand.BuildInstall(new ConfigService());
        Assert.That(install.Options.Select(o => o.Name), Is.EquivalentTo(
            new[] { "source", "name", "runner", "setting", "secrets-file", "secrets-stdin", "wait", "json" }));
        Assert.That(install.Description, Does.Contain("--secrets-file").And.Contain("hidden prompt"));
    }

    [Test]
    public void List_FiltersByTextCategorySourceAndInstalled() =>
        Assert.That(CatalogInstallCommand.BuildList(new ConfigService()).Options.Select(o => o.Name),
            Is.EquivalentTo(new[] { "search", "category", "source", "installed", "json" }));

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
}
