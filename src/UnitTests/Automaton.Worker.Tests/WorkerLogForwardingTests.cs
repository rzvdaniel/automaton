using System.Net;
using System.Net.Sockets;
using Automaton.Core.Logs;
using Automaton.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture, NonParallelizable]
public sealed class WorkerLogForwardingTests
{
    [TearDown]
    public void Reset() => LogForwarding.SetAddress(null);

    [Test]
    public async Task Logs_of_the_workers_host_reach_each_address_it_is_switched_to()
    {
        using var first = new Receiver();
        using var second = new Receiver();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Logging:File:Path"] = Path.Combine(Path.GetTempPath(), "worker-log-test-" + Guid.NewGuid().ToString("N"));
        WorkerHostApplication.ConfigureLogging(builder);
        using var host = builder.Build();
        _ = host.Services.GetService<Serilog.ILogger>();
        _ = host.Services.GetService<Serilog.IDiagnosticContext>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        LogForwarding.SetAddress(first.Address);
        logger.LogInformation("to first");
        Assert.That(await first.WaitForRequestAsync(), Is.True, "first address");

        first.Dispose();
        logger.LogInformation("to the dead first");
        await Task.Delay(3000);
        LogForwarding.SetAddress(second.Address);
        logger.LogInformation("to second");
        Assert.That(await second.WaitForRequestAsync(), Is.True, "second address");
    }

    [Test]
    public async Task Switching_away_from_an_address_that_never_answers_does_not_block()
    {
        var hang = new TcpListener(IPAddress.Loopback, 0);
        hang.Start();
        var accepted = new List<TcpClient>();
        _ = Task.Run(async () => { while (true) accepted.Add(await hang.AcceptTcpClientAsync()); });
        using var second = new Receiver();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Logging:File:Path"] = Path.Combine(Path.GetTempPath(), "worker-log-test-" + Guid.NewGuid().ToString("N"));
        WorkerHostApplication.ConfigureLogging(builder);
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        LogForwarding.SetAddress($"http://127.0.0.1:{((IPEndPoint)hang.LocalEndpoint).Port}");
        logger.LogInformation("to hang");
        await Task.Delay(3000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        LogForwarding.SetAddress(second.Address);
        logger.LogInformation("to second");
        Assert.That(await second.WaitForRequestAsync(), Is.True);
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "switching must not wait for the old address to time out");
        hang.Stop();
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
