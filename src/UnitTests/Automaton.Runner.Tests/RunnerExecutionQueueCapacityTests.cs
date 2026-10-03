using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerExecutionQueueCapacityTests
{
    [Test]
    public async Task FourRunningActionsAndQueuedExecutionDoNotBlockLogs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "automaton-capacity-" + Guid.NewGuid().ToString("N"));
        await using var harness = new SaturatedQueueHarness(directory);
        var actions = await harness.StartFourExecutionsAsync();
        var queued = harness.DeliverExecutionAsync();
        await harness.DeliverManagementAsync();
        harness.AssertCapacityAndLogs(queued);
        harness.ReleaseActions();
        await harness.WaitForActionsAsync(actions, queued);
        Assert.That(harness.ActionCalls, Is.EqualTo(5));
    }

    private static RunnerExecutionApiClient CreateApi()
        => new(new HttpClient(new Api()) { BaseAddress = new Uri("http://localhost/") });

    private sealed class SaturatedExecutor : IRunnerExecutionExecutor
    {
        private int actions;
        private int logs;
        public int ActionCalls => actions;
        public int LogReads => logs;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
        {
            if (request.Action!.Kind == "read-logs") Interlocked.Increment(ref logs);
            else
            {
                if (Interlocked.Increment(ref actions) == 4) Started.TrySetResult();
                await Finish.Task.WaitAsync(token);
            }
            return new() { Outcome = "Succeeded" };
        }
    }

    private sealed class Api : HttpMessageHandler
    {
        public static ConcurrentDictionary<Guid, bool> Management { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = Guid.Parse(request.RequestUri!.AbsolutePath.Split('/')[3]);
            var kind = Management[id] ? "read-logs" : "execute";
            object body = request.Method == HttpMethod.Get
                ? new RunnerExecutionAssignment("execute", new(null, null, AppActionCommand.Create(kind, new { })))
                : new RunnerExecutionReceipt("accepted");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }

    private sealed class SaturatedQueueHarness : IAsyncDisposable
    {
        private readonly string directory;
        private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        private readonly SaturatedExecutor executor = new();
        private readonly Broker broker = new();
        private readonly Guid runner = Guid.NewGuid();
        private readonly RunnerExecutionQueueConsumer consumer;
        private readonly Task consuming;

        public SaturatedQueueHarness(string directory)
        {
            this.directory = directory;
            consumer = new(new(new(new(directory), executor)));
            var api = CreateApi();
            var configuration = new HostedRunnerBrokerConfiguration("unused", 5671, "/", "test", "unused",
                "execution", true, "unused", "management");
            consuming = consumer.ConsumeAsync(broker.Connection(), configuration, api, runner, cancellation.Token);
        }

        public int ActionCalls => executor.ActionCalls;

        public async Task<Task[]> StartFourExecutionsAsync()
        {
            var actions = Enumerable.Range(0, 4)
                .Select(_ => DeliverExecutionAsync())
                .ToArray();
            await executor.Started.Task.WaitAsync(cancellation.Token);
            return actions;
        }

        public Task DeliverExecutionAsync()
            => broker.DeliverAsync("execution", runner, false, cancellation.Token);

        public Task DeliverManagementAsync()
            => broker.DeliverAsync("management", runner, true, cancellation.Token);

        public void AssertCapacityAndLogs(Task queued)
        {
            Assert.That(executor.LogReads, Is.EqualTo(1));
            Assert.That(executor.ActionCalls, Is.EqualTo(4));
            Assert.That(queued.IsCompleted, Is.False);
            Assert.That(broker.Queues["execution"].Capacity, Is.EqualTo(4));
            Assert.That(broker.Queues["management"].Capacity, Is.EqualTo(1));
            Assert.That(broker.Queues["execution"].DispatchConcurrency, Is.EqualTo(4));
            Assert.That(broker.Queues["management"].DispatchConcurrency, Is.EqualTo(1));
        }

        public void ReleaseActions() => executor.Finish.TrySetResult();

        public Task WaitForActionsAsync(Task[] actions, Task queued)
            => Task.WhenAll(actions.Append(queued)).WaitAsync(cancellation.Token);

        public async ValueTask DisposeAsync()
        {
            ReleaseActions();
            await cancellation.CancelAsync();
            try { await consuming; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            cancellation.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class Broker
    {
        public Dictionary<string, Queue> Queues { get; } = [];
        public IConnection Connection() => Proxy.Create<IConnection>((method, args) => method.Name == "CreateChannelAsync"
            ? Task.FromResult(Channel((CreateChannelOptions)args[0]!)) : throw new NotSupportedException(method.Name));
        private IChannel Channel(CreateChannelOptions options)
        {
            var queue = new Queue { DispatchConcurrency = options.ConsumerDispatchConcurrency };
            return Proxy.Create<IChannel>((method, args) => ChannelCall(queue, method, args));
        }
        private object? ChannelCall(Queue queue, MethodInfo method, object?[] args)
        {
            switch (method.Name)
            {
                case "BasicQosAsync": queue.Capacity = (ushort)args[1]!; queue.Slots = new(queue.Capacity); return Task.CompletedTask;
                case "BasicConsumeAsync":
                    queue.Consumer = (AsyncEventingBasicConsumer)args[6]!;
                    Queues.Add((string)args[0]!, queue); return Task.FromResult("consumer");
                case "BasicAckAsync": case "BasicRejectAsync": return ValueTask.CompletedTask;
                case "DisposeAsync": return ValueTask.CompletedTask;
                case "get_ContinuationTimeout": return TimeSpan.FromSeconds(5);
                default: throw new NotSupportedException(method.Name);
            }
        }
        public async Task DeliverAsync(string queueName, Guid runner, bool management, CancellationToken token)
        {
            var queue = Queues[queueName]; await queue.Slots.WaitAsync(token);
            try
            {
                var id = Guid.NewGuid(); Api.Management[id] = management;
                var body = JsonSerializer.SerializeToUtf8Bytes(new RunnerExecutionCommand(id, runner), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await queue.Consumer.HandleBasicDeliverAsync("consumer", 1, false, "", queueName,
                    new BasicProperties { MessageId = id.ToString() }, body, token);
                Api.Management.TryRemove(id, out _);
            }
            finally { queue.Slots.Release(); }
        }
    }
    private sealed class Queue
    {
        public ushort Capacity { get; set; }
        public ushort? DispatchConcurrency { get; init; }
        public SemaphoreSlim Slots { get; set; } = null!;
        public AsyncEventingBasicConsumer Consumer { get; set; } = null!;
    }
    public class Proxy : DispatchProxy
    {
        private Func<MethodInfo, object?[], object?> call = null!;
        public static T Create<T>(Func<MethodInfo, object?[], object?> call) where T : class
        {
            var result = Create<T, Proxy>(); ((Proxy)(object)result).call = call; return result;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => call(targetMethod!, args!);
    }
}
