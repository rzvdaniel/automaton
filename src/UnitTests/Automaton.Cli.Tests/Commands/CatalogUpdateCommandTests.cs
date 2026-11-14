using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CatalogUpdateCommandTests
{
    [Test]
    public void Update_TakesTheAppAndNoOptionThatCarriesASecretValue()
    {
        var update = CatalogUpdateCommand.Build(new ConfigService());

        Assert.That(update.Name, Is.EqualTo("update"));
        Assert.That(update.Arguments.Select(a => a.Name), Is.EqualTo(new[] { "app" }));
        Assert.That(update.Options.Select(o => o.Name), Is.EquivalentTo(
            new[] { "check", "confirm-edited", "secrets-file", "secrets-stdin", "wait", "json" }));
        Assert.That(update.Description, Does.Contain("--confirm-edited").And.Contain("--secrets-file"));
    }

    [Test]
    public void Catalog_HasTheUpdateCommand() =>
        Assert.That(CatalogCommand.Build(new ConfigService()).Subcommands.Select(c => c.Name), Does.Contain("update"));

    [Test]
    public void Short_CutsACommitToSevenCharacters()
    {
        Assert.That(CatalogUpdateCommand.Short("0123456789abcdef"), Is.EqualTo("0123456"));
        Assert.That(CatalogUpdateCommand.Short(""), Is.EqualTo(""));
    }
}

[TestFixture]
public class PermissionsAdminCommandsTests
{
    [Test]
    public void Permissions_HasListApproveAndReject()
    {
        var names = PermissionsCommand.Build(new ConfigService()).Subcommands.Select(c => c.Name);
        Assert.That(names, Is.SupersetOf(new[] { "status", "list", "approve", "reject" }));
    }

    [Test]
    public void Approve_TakesAppPermissionAndRunner()
    {
        var approve = PermissionsAdminCommands.BuildDecision(new ConfigService(), approve: true);
        Assert.That(approve.Arguments.Select(a => a.Name), Is.EqualTo(new[] { "app", "permission" }));
        Assert.That(approve.Options.Select(o => o.Name), Is.EquivalentTo(new[] { "runner", "json" }));
        Assert.That(PermissionsAdminCommands.BuildDecision(new ConfigService(), approve: false).Name, Is.EqualTo("reject"));
    }

    [TestCase("runner:maintain", null, false)]
    [TestCase("runner:maintain", "  ", false)]
    [TestCase("runner:maintain", "build-1", true)]
    [TestCase("backups:read", null, true)]
    [TestCase("backups:read", "build-1", false)]
    public void RunnerOption_IsRequiredOnlyForRunnerMaintain(string permission, string? runner, bool valid)
    {
        if (valid) Assert.DoesNotThrow(() => PermissionsAdminCommands.CheckRunnerOption(permission, runner));
        else Assert.Throws<InvalidOperationException>(() => PermissionsAdminCommands.CheckRunnerOption(permission, runner));
    }

    private static System.Text.Json.JsonElement Items(params (string Id, string Name)[] apps) =>
        System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(apps.Select(a => new { id = a.Id, name = a.Name }))).RootElement;

    [Test]
    public void PickApp_MatchesTheNameIgnoringCase()
        => Assert.That(PermissionsAdminCommands.PickApp(Items(("1", "Harbour"), ("2", "Other")), "harbour"), Is.EqualTo("1"));

    [Test]
    public void PickApp_RefusesNoMatchAndAmbiguousNames()
    {
        Assert.Throws<InvalidOperationException>(() => PermissionsAdminCommands.PickApp(Items(("1", "Harbour")), "missing"));
        Assert.Throws<InvalidOperationException>(() => PermissionsAdminCommands.PickApp(Items(("1", "Harbour"), ("2", "harbour")), "Harbour"));
    }
}
