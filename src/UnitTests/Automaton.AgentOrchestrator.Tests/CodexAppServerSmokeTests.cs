using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Orchestration;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class CodexAppServerSmokeTests
{
    [Test]
    [Category("CodexAppServerSmoke")]
    public async Task InstalledAppServer_CompletesInitializeHandshakeOnly()
    {
        var command = Environment.GetEnvironmentVariable("AUTOMATON_CODEX_SMOKE_COMMAND");
        if (string.IsNullOrWhiteSpace(command)) Assert.Ignore("Set AUTOMATON_CODEX_SMOKE_COMMAND for the controlled local smoke test.");
        var transport = new ProcessCodexJsonRpcTransport(new(
            command,
            TestContext.CurrentContext.WorkDirectory,
            ShutdownTimeout: TimeSpan.FromSeconds(5)));
        await using var client = new CodexAppServerClient(transport, new NoopCodexExecutionOwnership(), new SystemClock(), TimeSpan.FromSeconds(10), TestContext.CurrentContext.WorkDirectory);

        await client.InitializeAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.ProcessIdentity, Is.Not.Null);
            Assert.That(client.ProcessIdentity!.LaunchNonce, Is.EqualTo(transport.LaunchNonce));
            Assert.That(client.Events, Has.Some.Matches<Automaton.AgentOrchestrator.Domain.CodexSessionEvent>(x => x.Kind == Automaton.AgentOrchestrator.Domain.CodexSessionEventKinds.Initialized));
        });
    }
}
