using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class ProjectCommandTests
{
    private static JsonElement Projects(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string Two = """[{"id":"11111111-1111-1111-1111-111111111111","name":"alpha","displayName":"Alpha"},{"id":"22222222-2222-2222-2222-222222222222","name":"beta","displayName":"Beta"}]""";

    [Test]
    public void ExposesTheProjectSubcommands() =>
        Assert.That(ProjectCommand.Build(new ConfigService()).Subcommands.Select(item => item.Name),
            Is.EquivalentTo(new[] { "create", "list", "add-app", "remove-app", "runners" }));

    [Test]
    public void ExposesRunnerAndDeleteCommands()
    {
        Assert.That(ProjectRunnersCommand.Build(new ConfigService()).Subcommands.Select(item => item.Name),
            Is.EquivalentTo(new[] { "list", "add", "remove" }));
        Assert.That(RunnersCommand.Build(new ConfigService()).Subcommands.Select(item => item.Name), Does.Contain("delete"));
        Assert.That(DeleteCommand.Build(new ConfigService()).Options.Select(item => item.Name), Does.Contain("yes"));
    }

    [Test]
    public void RunnerReferencesResolveOnlyWithinTheReturnedScope()
    {
        var runners = Projects("""[{"id":"11111111-1111-1111-1111-111111111111","name":"Mine"}]""");
        Assert.That(RunnerReferenceResolver.Pick(runners, "MINE", "id", "name"),
            Is.EqualTo("11111111-1111-1111-1111-111111111111"));
        Assert.Throws<InvalidOperationException>(() => RunnerReferenceResolver.Pick(runners, "Other", "id", "name"));
    }

    [Test]
    public void PicksTheOnlyProjectWhenNoneIsNamed() =>
        Assert.That(ProjectCommand.PickProject(Projects("""[{"id":"p1","name":"alpha","displayName":"Alpha"}]"""), null).Id, Is.EqualTo("p1"));

    [Test]
    public void PicksByNameOrId()
    {
        Assert.That(ProjectCommand.PickProject(Projects(Two), "BETA").Id, Does.StartWith("2222"));
        Assert.That(ProjectCommand.PickProject(Projects(Two), "11111111-1111-1111-1111-111111111111").DisplayName, Is.EqualTo("Alpha"));
    }

    [Test]
    public void RefusesWhenNothingOrSeveralMatch()
    {
        Assert.Throws<InvalidOperationException>(() => ProjectCommand.PickProject(Projects(Two), null));
        Assert.Throws<InvalidOperationException>(() => ProjectCommand.PickProject(Projects(Two), "gamma"));
        Assert.Throws<InvalidOperationException>(() => ProjectCommand.PickProject(Projects("[]"), null));
    }
}
