using Automaton.Node.Preparation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodePreparationLifecycleTests
{
    [Test]
    public async Task HostStartsRecoveryRequiredAndRepeatedStopIsSafe()
    {
        var installationLock = new FakeInstallationLock();
        using var host = NodePreparationHost.CreateBuilderForTests(installationLock).Build();
        var state = host.Services.GetRequiredService<NodePreparationState>();
        installationLock.Observe(state);
        Assert.That(state.Phase, Is.EqualTo(NodePreparationPhase.Created));
        await host.StartAsync();
        Assert.That(state.Phase, Is.EqualTo(NodePreparationPhase.RecoveryRequired));
        Assert.That(installationLock.IsHeld, Is.True);
        Assert.That(state.Capabilities.CanAdmitWorkloads, Is.False);
        Assert.That(state.Capabilities.Report, Is.Null);
        await host.StopAsync();
        await host.StopAsync();
        Assert.That(state.Phase, Is.EqualTo(NodePreparationPhase.Stopped));
        Assert.That(installationLock.IsHeld, Is.False);
        Assert.That(installationLock.ReleaseObservedStoppedState, Is.True);
    }

    [Test]
    public void CancellationBeforeStartLeavesPreparationUnstarted()
    {
        var installationLock = new FakeInstallationLock();
        using var host = NodePreparationHost.CreateBuilderForTests(installationLock).Build();
        var service = host.Services.GetServices<IHostedService>().Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(() => service.StartAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(host.Services.GetRequiredService<NodePreparationState>().Phase,
            Is.EqualTo(NodePreparationPhase.Created));
        Assert.That(installationLock.AcquireCount, Is.Zero);
    }

    [Test]
    public async Task RunHonorsCancellationAndLogsOnlyPreparationMessages()
    {
        var builder = NodePreparationHost.CreateBuilderForTests(new FakeInstallationLock());
        var logs = new CapturedLogs();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        using var host = builder.Build();
        var state = host.Services.GetRequiredService<NodePreparationState>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted.Register(() => started.TrySetResult());
        using var cancellation = new CancellationTokenSource();
        var running = host.RunAsync(cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(state.Phase, Is.EqualTo(NodePreparationPhase.Stopped));
            Assert.That(logs.Messages, Is.EqualTo(new[]
            {
                "Node installation lock held; local authority unavailable; RecoveryRequired; admission disabled.",
                "Node preparation stopped; installation lock released."
            }));
        }
        finally
        {
            await cancellation.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void HostHasNoCredentialConfigurationOrOperationalHostedServices()
    {
        var builder = NodePreparationHost.CreateBuilderForTests(new FakeInstallationLock());
        Assert.That(((IConfigurationBuilder)builder.Configuration).Sources, Is.Empty);
        using var host = builder.Build();
        var services = host.Services.GetServices<IHostedService>().Select(s => s.GetType().FullName).ToArray();
        Assert.That(services, Is.EqualTo(new[] { "Automaton.Node.Preparation.NodePreparationService" }));
        Assert.That(host.Services.GetService<HttpClient>(), Is.Null);
    }

    [Test]
    public async Task StopIsTerminalEvenWhenCalledBeforeStartOrWithCancelledToken()
    {
        var installationLock = new FakeInstallationLock();
        using var host = NodePreparationHost.CreateBuilderForTests(installationLock).Build();
        var service = host.Services.GetServices<IHostedService>().Single();
        await service.StopAsync(new CancellationToken(canceled: true));
        await service.StartAsync(CancellationToken.None);
        Assert.That(host.Services.GetRequiredService<NodePreparationState>().Phase,
            Is.EqualTo(NodePreparationPhase.Stopped));
        Assert.That(installationLock.IsHeld, Is.False);
    }

    [Test]
    public void StartupCancellationAfterFakeLockAcquisitionReleasesOwnership()
    {
        using var cancellation = new CancellationTokenSource();
        var installationLock = new FakeInstallationLock { CancelAfterAcquire = cancellation };
        using var host = NodePreparationHost.CreateBuilderForTests(installationLock).Build();
        var service = host.Services.GetServices<IHostedService>().Single();
        Assert.That(() => service.StartAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(installationLock.IsHeld, Is.False);
        Assert.That(host.Services.GetRequiredService<NodePreparationState>().Phase,
            Is.EqualTo(NodePreparationPhase.Created));
    }

    private sealed class FakeInstallationLock : INodeInstallationLock
    {
        private NodePreparationState? state;
        public bool IsHeld { get; private set; }
        public int AcquireCount { get; private set; }
        public CancellationTokenSource? CancelAfterAcquire { get; init; }
        public bool ReleaseObservedStoppedState { get; private set; }

        public void Observe(NodePreparationState observedState) => state = observedState;

        public void Acquire(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCount++;
            IsHeld = true;
            CancelAfterAcquire?.Cancel();
        }

        public void Release()
        {
            IsHeld = false;
            ReleaseObservedStoppedState |= state?.Phase == NodePreparationPhase.Stopped;
        }
    }

    private sealed class CapturedLogs : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Messages.Add(formatter(state, exception));
        }
    }
}
