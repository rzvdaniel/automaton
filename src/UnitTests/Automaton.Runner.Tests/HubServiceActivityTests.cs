using System.Reflection;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[TestFixture]
public sealed class HubServiceActivityTests
{
    private static readonly MethodInfo HandleReconnectWhenIdleMethod =
        typeof(HubService).GetMethod("HandleReconnectWhenIdle", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(HubService), "HandleReconnectWhenIdle");
    private static readonly MethodInfo WaitForReconnectRequestMethod =
        typeof(HubService).GetMethod("WaitForReconnectRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(HubService), "WaitForReconnectRequestAsync");
    private static readonly FieldInfo ActiveOperationCountField =
        typeof(HubService).GetField("activeOperationCount", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HubService), "activeOperationCount");
    private static readonly FieldInfo ReconnectRequestedField =
        typeof(HubService).GetField("reconnectRequested", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HubService), "reconnectRequested");

    [Test]
    public async Task PackageDeploymentActivityDefersReconnectUntilDeploymentCompletes()
    {
        var service = CreateService();
        var deploymentStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDeployment = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var deploymentTask = InvokeActivityAsync(service, async () =>
        {
            deploymentStarted.SetResult(true);
            await releaseDeployment.Task;
            return true;
        });

        await deploymentStarted.Task;
        var connectionLoopWait = WaitForReconnectRequestAsync(service);
        Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(1));

        HandleReconnectWhenIdleMethod.Invoke(service, ["API cutover"]);

        Assert.Multiple(() =>
        {
            Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(1));
            Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(1));
            Assert.That(connectionLoopWait.IsCompleted, Is.False);
        });

        releaseDeployment.SetResult(true);
        Assert.That(await deploymentTask, Is.True);
        Assert.That(await connectionLoopWait.WaitAsync(TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(0));
        Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(0));
    }

    [Test]
    public async Task ReconnectWhenIdle_WakesConnectionLoopImmediatelyWhenNoWorkIsActive()
    {
        var service = CreateService();
        var connectionLoopWait = WaitForReconnectRequestAsync(service);

        HandleReconnectWhenIdleMethod.Invoke(service, ["idle"]);

        Assert.That(await connectionLoopWait.WaitAsync(TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(0));
        Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(0));
    }

    [Test]
    public async Task ReconnectWhenIdle_ConsumesPendingRequestWhenActivityFinishesWithoutWakeup()
    {
        var service = CreateService();
        ActiveOperationCountField.SetValue(service, 0);
        ReconnectRequestedField.SetValue(service, 1);

        var reconnectRequested = await WaitForReconnectRequestAsync(service, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(reconnectRequested, Is.True);
            Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task StopHeartbeatLoop_CancelsAndAwaitsItsOwnedTask()
    {
        var heartbeat = new HubHeartbeatService(() => null, Serilog.Log.Logger);
        using var serviceLifetime = new CancellationTokenSource();

        await heartbeat.StartAsync(serviceLifetime.Token);

        var tokenSourceField = typeof(HubHeartbeatService)
            .GetField("tokenSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var taskField = typeof(HubHeartbeatService)
            .GetField("heartbeatTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tokenSource = (CancellationTokenSource?)tokenSourceField.GetValue(heartbeat);
        var task = (Task?)taskField.GetValue(heartbeat);
        Assert.That(tokenSource, Is.Not.Null);
        Assert.That(task, Is.Not.Null);
        Assert.That(task!.IsCompleted, Is.False);

        await heartbeat.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tokenSource!.IsCancellationRequested, Is.True);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(tokenSourceField.GetValue(heartbeat), Is.Null);
            Assert.That(taskField.GetValue(heartbeat), Is.Null);
        });
    }

    private static async Task<T> InvokeActivityAsync<T>(HubService service, Func<Task<T>> operation)
    {
        return await service.ExecuteWithRunnerActivityAsync(operation);
    }

    private static Task<bool> WaitForReconnectRequestAsync(
        HubService service,
        TimeSpan? interval = null)
        => (Task<bool>)WaitForReconnectRequestMethod.Invoke(
            service,
            [interval ?? TimeSpan.FromSeconds(30), CancellationToken.None])!;

    private static HubService CreateService() => new(
        null!, null!, null!, null!, null!, null!, null!, null!);
}
