using System.Text.Json;
using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public sealed class ActionWebhookCommandTests
{
    private static readonly JsonElement Hooks = JsonDocument.Parse("""
        [
          {"id":"11111111-1111-1111-1111-111111111111","actionKey":"docs-walk","name":"Docs walk","enabled":true,"url":"https://api.test/api/hooks/1","calls":[{"calledAtUtc":"2026-11-21T10:00:00Z","outcome":"Started"}]},
          {"id":"22222222-2222-2222-2222-222222222222","actionKey":"upgrade","name":"Upgrade","enabled":false,"url":"https://api.test/api/hooks/2","calls":[]},
          {"id":"33333333-3333-3333-3333-333333333333","actionKey":"upgrade","name":"Upgrade again","enabled":true,"url":"https://api.test/api/hooks/3","calls":[]}
        ]
        """).RootElement.Clone();

    [Test]
    public void Pick_FindsTheOnlyWebhookOfAnAction()
        => Assert.That(ActionWebhookCommand.Pick(Hooks, "docs-walk", null).GetProperty("name").GetString(), Is.EqualTo("Docs walk"));

    [Test]
    public void Pick_AsksForAName_WhenTheActionHasSeveral()
    {
        Assert.That(() => ActionWebhookCommand.Pick(Hooks, "upgrade", null), Throws.InvalidOperationException.With.Message.Contain("--name"));
        Assert.That(ActionWebhookCommand.Pick(Hooks, "upgrade", "upgrade again").GetProperty("enabled").GetBoolean(), Is.True);
    }

    [Test]
    public void Pick_SaysSo_WhenThereIsNoWebhook()
        => Assert.That(() => ActionWebhookCommand.Pick(Hooks, "other", null), Throws.InvalidOperationException.With.Message.Contain("no matching webhook"));

    [Test]
    public void Describe_ShowsStateAndLastCall()
    {
        Assert.That(ActionWebhookCommand.Describe(Hooks[0]), Does.Contain("on").And.Contain("last call Started"));
        Assert.That(ActionWebhookCommand.Describe(Hooks[1]), Does.Contain("off").And.Contain("not called yet"));
    }
}
