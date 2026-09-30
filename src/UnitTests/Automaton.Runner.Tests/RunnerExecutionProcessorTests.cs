using NUnit.Framework;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerExecutionProcessorTests
{
    private string directory = null!;
    [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "automaton-execution-test-" + Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Test]
    public async Task LostCallbackAndRunnerRestartReplayCompletedWorkWithoutExecutingAgain()
    {
        var executor = new Executor(); var id = Guid.NewGuid(); var journal = new RunnerExecutionJournal(directory);
        var first = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        var replay = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        Assert.That(executor.Calls, Is.EqualTo(1)); Assert.That(replay.Output, Is.EqualTo(first.Output));
    }

    [Test]
    public async Task AcceptedButUncertainWorkIsTerminalAndNeverRunsAgain()
    {
        var journal = new RunnerExecutionJournal(directory); var id = Guid.NewGuid();
        using (journal.Lock(id)) journal.Accept(id);
        var executor = new Executor();
        var result = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo("Lost")); Assert.That(executor.Calls, Is.Zero);
        Assert.That((await journal.ReadAsync(id, CancellationToken.None))!.Outcome, Is.EqualTo("Lost"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task JournalWriteFailureDoesNotReportCompletedWorkAsFailedOrRepeatIt(bool cancel)
    {
        var journal = new RunnerExecutionJournal(directory); var id = Guid.NewGuid(); var executor = new Executor();
        var processor = new RunnerExecutionProcessor(journal, executor);
        Directory.CreateDirectory(Path.Combine(directory, $"{id:N}.json.tmp"));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => processor.ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None));
        Directory.Delete(Path.Combine(directory, $"{id:N}.json.tmp"));
        var replay = cancel ? await processor.CancelAsync(id, CancellationToken.None)
            : await processor.ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        Assert.That(replay.Outcome, Is.EqualTo("Succeeded")); Assert.That(executor.Calls, Is.EqualTo(1));
    }

    [Test]
    public void JournalSerializesCompetingProcesses()
    {
        var id = Guid.NewGuid(); var journal = new RunnerExecutionJournal(directory);
        using var ownership = journal.Lock(id);
        Assert.Throws<IOException>(() => { using var competing = journal.Lock(id); });
    }

    [Test]
    public async Task CancellationPersistsTerminalResultDespiteCancelledExecutionToken()
    {
        var executor = new Executor { Cancel = true }; var id = Guid.NewGuid(); var journal = new RunnerExecutionJournal(directory);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, new(Guid.NewGuid(), null), cancellation.Token);
        Assert.That(result.Outcome, Is.EqualTo("Cancelled"));
        Assert.That((await journal.ReadAsync(id, CancellationToken.None))!.Outcome, Is.EqualTo("Cancelled"));
    }

    private sealed class Executor : IRunnerExecutionExecutor
    {
        public int Calls { get; private set; }
        public bool Cancel { get; init; }
        public Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
        {
            Calls++; if (Cancel) token.ThrowIfCancellationRequested();
            return Task.FromResult(new RunnerExecutionResult { Outcome = "Succeeded", Output = "marker" });
        }
    }
}
