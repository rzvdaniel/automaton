using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Orchestration;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class CodexAppServerSmokeTests
{
    [Test]
    [Category("CodexAppServerSmoke")]
    public async Task InstalledAppServer_InitializesAndStartsThreadInTemporaryWorkspace()
    {
        var command = Environment.GetEnvironmentVariable("AUTOMATON_CODEX_SMOKE_COMMAND");
        if (string.IsNullOrWhiteSpace(command)) Assert.Ignore("Set AUTOMATON_CODEX_SMOKE_COMMAND for the controlled local smoke test.");
        var workspace = Directory.CreateTempSubdirectory("automaton-codex-smoke-");
        try
        {
            var workspacePath = Path.TrimEndingDirectorySeparator(workspace.FullName);
            var transport = new ProcessCodexJsonRpcTransport(new(
                command,
                workspacePath,
                ShutdownTimeout: TimeSpan.FromSeconds(5)));
            await using var client = new CodexAppServerClient(transport, new NoopCodexExecutionOwnership(), new SystemClock(), TimeSpan.FromSeconds(10), workspacePath);

            await client.InitializeAsync(CancellationToken.None);
            var thread = await client.StartThreadAsync(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(client.ProcessIdentity, Is.Not.Null);
                Assert.That(client.ProcessIdentity!.LaunchNonce, Is.EqualTo(transport.LaunchNonce));
                Assert.That(thread.Id, Is.Not.Empty);
                Assert.That(client.Events, Has.Some.Matches<Automaton.AgentOrchestrator.Domain.CodexSessionEvent>(x => x.Kind == Automaton.AgentOrchestrator.Domain.CodexSessionEventKinds.Initialized));
                Assert.That(client.Events, Has.Some.Matches<Automaton.AgentOrchestrator.Domain.CodexSessionEvent>(x => x.Kind == Automaton.AgentOrchestrator.Domain.CodexSessionEventKinds.ThreadStarted));
            });
        }
        finally
        {
            if (workspace.Exists) workspace.Delete(recursive: true);
        }
    }
}
