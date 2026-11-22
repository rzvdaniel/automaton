using System.Net;
using System.Net.Sockets;
using Automaton.Core.Logs;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Serilog;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class RunnerLogForwardingTests
{
    [TearDown]
    public void Reset()
    {
        LogForwarding.SetAddress(null);
        Log.Logger = Serilog.Core.Logger.None;
    }

    [Test]
    public async Task Logs_of_the_runner_reach_each_address_it_is_switched_to()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var logger = Program.CreateLogger(configuration);
        Log.Logger = logger;
        using var first = new Receiver();
        using var second = new Receiver();

        Assert.That(LogForwarding.IsSwitchable, Is.True);
        LogForwarding.SetAddress(first.Address);
        Log.ForContext<RunnerLogForwardingTests>().Information("to first");
        Assert.That(await first.WaitForRequestAsync(), Is.True, "first address");

        first.Dispose();
        LogForwarding.SetAddress(second.Address);
        Log.ForContext<RunnerLogForwardingTests>().Information("to second");
        Assert.That(await second.WaitForRequestAsync(), Is.True, "second address");
    }

    private sealed class Receiver : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly TaskCompletionSource got = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Address { get; }

        public Receiver()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var number = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Address = $"http://127.0.0.1:{number}";
            listener.Prefixes.Add(Address + "/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    try
                    {
                        var context = await listener.GetContextAsync();
                        got.TrySetResult();
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                    }
                    catch { return; }
                }
            });
        }

        public async Task<bool> WaitForRequestAsync() => await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(20))) == got.Task;
        public void Dispose() => listener.Close();
    }
}
