using NUnit.Framework;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;

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

    [TestCase(false)]
    [TestCase(true)]
    public void MachineManagementRunnerRejectsRawWorkflowAndCustomStepPayloads(bool customStep)
    {
        var previousProfile = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", RunnerApplicationProfiles.MachineManagement);
        try
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            using var executor = new RunnerExecutionExecutor(services);
            var request = customStep
                ? new RunnerExecutionRequest(null, new() { Id = Guid.NewGuid(), Code = "print('unsafe')", Language = "python" })
                : new RunnerExecutionRequest(Guid.NewGuid(), null);

            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await executor.ExecuteAsync(request, CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", previousProfile);
        }
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

    [Test]
    public async Task ConcurrentActionRedeliverySharesResultWithoutRepeatingWork()
    {
        var executor = new BlockingExecutor(); var id = Guid.NewGuid(); var processor = new RunnerExecutionProcessor(new(directory), executor);
        var request = CreateActionRequest(id);
        var first = processor.ProcessAsync(id, request, CancellationToken.None);
        await executor.Started.Task;
        var duplicate = processor.ProcessAsync(id, request, CancellationToken.None);
        executor.Finish.SetResult();
        var results = await Task.WhenAll(first, duplicate).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(executor.Calls, Is.EqualTo(1)); Assert.That(results.Select(r => r.Output), Is.All.EqualTo("once"));
    }
    [Test]
    public async Task StaleAssignmentAfterConfirmedResultCleanupNeverExecutesAgain()
    {
        var executor = new Executor(); var id = Guid.NewGuid(); var journal = new RunnerExecutionJournal(directory);
        var processor = new RunnerExecutionProcessor(journal, executor);
        await processor.ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        processor.Forget(id);
        var stale = await processor.ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        Assert.That(stale.Outcome, Is.EqualTo("Lost")); Assert.That(executor.Calls, Is.EqualTo(1));
    }
    [Test]
    public async Task CancelThenConfirmedCleanupAndRestartRejectsStaleExecution()
    {
        var executor = new Executor(); var id = Guid.NewGuid(); var journal = new RunnerExecutionJournal(directory);
        var processor = new RunnerExecutionProcessor(journal, executor);
        Assert.That((await processor.CancelAsync(id, CancellationToken.None)).Outcome, Is.EqualTo("Cancelled"));
        processor.Forget(id);
        Assert.That(await journal.ReadAsync(id, CancellationToken.None), Is.Null);
        var restarted = new RunnerExecutionProcessor(new(directory), executor);
        var stale = await restarted.ProcessAsync(id, new(null, null, AppActionCommand.Create("execute", new { })), CancellationToken.None);
        Assert.That(stale.Outcome, Is.EqualTo("Cancelled"));
        Assert.That(executor.Calls, Is.Zero);
    }
    [Test]
    public async Task ConfirmedCleanupDoesNotDeleteAConcurrentJournalWritersFiles()
    {
        var id = Guid.NewGuid(); var journal = new RunnerExecutionJournal(directory);
        await new RunnerExecutionProcessor(journal, new Executor()).ProcessAsync(id, new(Guid.NewGuid(), null), CancellationToken.None);
        using (journal.Lock(id))
        {
            journal.Forget(id);
            Assert.That(await journal.ReadAsync(id, CancellationToken.None), Is.Not.Null);
        }
        journal.Forget(id);
        Assert.That(await journal.ReadAsync(id, CancellationToken.None), Is.Null);
        Assert.That(journal.WasAccepted(id), Is.True);
    }
    private sealed class BlockingExecutor : IRunnerExecutionExecutor
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
        {
            Calls++; Started.TrySetResult(); await Finish.Task.WaitAsync(token);
            return new() { Outcome = "Succeeded", Output = "once" };
        }
    }

    private static RunnerExecutionRequest CreateActionRequest(Guid id)
        => new(null, null, AppActionCommand.Create("execute", new ExecuteAppActionModuleRequest
        {
            ExecutionId = id,
            Input = System.Text.Json.JsonSerializer.SerializeToElement(new { })
        }));

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
