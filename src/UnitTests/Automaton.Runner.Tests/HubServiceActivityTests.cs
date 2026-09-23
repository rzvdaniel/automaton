using System.Reflection;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[TestFixture]
public sealed class HubServiceActivityTests
{
    private static readonly MethodInfo ExecuteWithRunnerActivityMethod =
        typeof(HubService).GetMethod("ExecuteWithRunnerActivityAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(HubService), "ExecuteWithRunnerActivityAsync");
    private static readonly MethodInfo HandleReconnectWhenIdleMethod =
        typeof(HubService).GetMethod("HandleReconnectWhenIdle", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(HubService), "HandleReconnectWhenIdle");
    private static readonly FieldInfo ActiveOperationCountField =
        typeof(HubService).GetField("activeOperationCount", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HubService), "activeOperationCount");
    private static readonly FieldInfo ReconnectRequestedField =
        typeof(HubService).GetField("reconnectRequested", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HubService), "reconnectRequested");
    private static readonly FieldInfo HeartbeatTokenSourceField =
        typeof(HubService).GetField("heartbeatTokenSource", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HubService), "heartbeatTokenSource");
    private static readonly FieldInfo HeartbeatTaskField =
        typeof(HubService).GetField("heartbeatTask", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HubService), "heartbeatTask");
    private static readonly MethodInfo StartHeartbeatLoopMethod =
        typeof(HubService).GetMethod("StartHeartbeatLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(HubService), "StartHeartbeatLoopAsync");
    private static readonly MethodInfo StopHeartbeatLoopMethod =
        typeof(HubService).GetMethod("StopHeartbeatLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(HubService), "StopHeartbeatLoopAsync");

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
        Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(1));

        HandleReconnectWhenIdleMethod.Invoke(service, ["API cutover"]);

        Assert.Multiple(() =>
        {
            Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(1));
            Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(1));
        });

        releaseDeployment.SetResult(true);
        Assert.That(await deploymentTask, Is.True);
        Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(0));
        Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(0));
    }

    [Test]
    public void ReconnectWhenIdle_StillQueuesImmediatelyWhenNoWorkIsActive()
    {
        var service = CreateService();

        HandleReconnectWhenIdleMethod.Invoke(service, ["idle"]);

        Assert.That(ActiveOperationCountField.GetValue(service), Is.EqualTo(0));
        Assert.That(ReconnectRequestedField.GetValue(service), Is.EqualTo(0));
    }

    [Test]
    public async Task StopHeartbeatLoop_CancelsAndAwaitsItsOwnedTask()
    {
        var service = CreateService();
        using var serviceLifetime = new CancellationTokenSource();

        await InvokeHeartbeatLifecycleAsync(StartHeartbeatLoopMethod, service, serviceLifetime.Token);

        var tokenSource = (CancellationTokenSource?)HeartbeatTokenSourceField.GetValue(service);
        var task = (Task?)HeartbeatTaskField.GetValue(service);
        Assert.That(tokenSource, Is.Not.Null);
        Assert.That(task, Is.Not.Null);
        Assert.That(task!.IsCompleted, Is.False);

        await InvokeHeartbeatLifecycleAsync(StopHeartbeatLoopMethod, service);

        Assert.Multiple(() =>
        {
            Assert.That(tokenSource!.IsCancellationRequested, Is.True);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(HeartbeatTokenSourceField.GetValue(service), Is.Null);
            Assert.That(HeartbeatTaskField.GetValue(service), Is.Null);
        });
    }

    private static Task InvokeHeartbeatLifecycleAsync(MethodInfo method, HubService service, params object[] arguments)
        => (Task)method.Invoke(service, arguments)!;

    private static async Task<T> InvokeActivityAsync<T>(HubService service, Func<Task<T>> operation)
    {
        var method = ExecuteWithRunnerActivityMethod.MakeGenericMethod(typeof(T));
        var task = (Task<T>)method.Invoke(service, [operation])!;
        return await task;
    }

    private static HubService CreateService() => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
}
