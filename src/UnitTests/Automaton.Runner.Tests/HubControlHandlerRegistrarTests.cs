using Automaton.Runner.Console.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class HubControlHandlerRegistrarTests
{
    [TestCase("WelcomeRunner")]
    [TestCase("ApiInstanceIdentity")]
    public async Task RollbackIdentityConfirmsLatestTargetAndAdmitsTunnels(string method)
    {
        await using var fixture = await IdentityConnection.CreateAsync();
        var state = fixture.State;

        state.SetConnectedApiColor("blue");
        Assert.That(state.RequestReconnect("green"), Is.True);
        state.SetConnectedApiColor(null);
        state.Reset();
        Assert.That(state.RequestReconnect("blue"), Is.True);
        Assert.That(state.TryBeginTunnelRequest(), Is.False);

        // A connection to the superseded forward target finishes after rollback was requested.
        await fixture.SendIdentityAsync(method, "superseded-api", "green");
        Assert.That(state.ShouldReconnect, Is.True);
        Assert.That(state.TryBeginTunnelRequest(), Is.False);
        state.Reset();
        state.SetConnectedApiColor(null);

        var identity = await fixture.SendIdentityAsync(method, "retained-api", "blue");
        Assert.That(identity,
            Is.EqualTo(("retained-api", "blue")));
        Assert.That(state.ShouldReconnect, Is.False);
        Assert.That(state.TryBeginTunnelRequest(), Is.True);
        Assert.That(state.CompleteTunnelRequest(), Is.False);
    }

    private sealed class IdentityConnection(WebApplication app, HubConnection connection) : IAsyncDisposable
    {
        private readonly Serilog.Core.Logger logger = new Serilog.LoggerConfiguration().CreateLogger();
        private TaskCompletionSource<(string?, string?)> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RunnerTunnelReconnectState State { get; } = new();

        public static async Task<IdentityConnection> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSignalR();
            var app = builder.Build();
            app.MapHub<IdentityHub>("/identity");
            await app.StartAsync();
            var connection = new HubConnectionBuilder().WithUrl($"{app.Urls.Single()}/identity").Build();
            var fixture = new IdentityConnection(app, connection);
            fixture.RegisterIdentityHandler();
            await connection.StartAsync();
            return fixture;
        }

        private void RegisterIdentityHandler()
        {
            new HubControlHandlerRegistrar(app.Services, logger).Register(
                connection,
                (id, color) =>
                {
                    State.SetConnectedApiColor(color);
                    received.TrySetResult((id, color));
                },
                () => { }, _ => { }, () => false, _ => string.Empty, () => Task.CompletedTask);
        }

        public async Task<(string?, string?)> SendIdentityAsync(string method, string instanceId, string color)
        {
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await connection.InvokeAsync("SendIdentity", method, instanceId, color);
            return await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            await logger.DisposeAsync();
            await app.DisposeAsync();
        }
    }

    public sealed class IdentityHub : Hub
    {
        public Task SendIdentity(string method, string instanceId, string color)
        {
            if (method == "ApiInstanceIdentity")
                return Clients.Caller.SendAsync(method, instanceId, color);

            // Actual single-object WelcomeRunner shape from API revision e71ca380.
            var payload = new
            {
                Message = "Welcome test-runner",
                ApiInstanceId = instanceId,
                ApiColor = color
            };
            return Clients.Caller.SendAsync(method, payload);
        }
    }
}
