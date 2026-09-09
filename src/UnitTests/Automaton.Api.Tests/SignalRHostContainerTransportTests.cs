using Automaton.Api.Hubs;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class SignalRHostContainerTransportTests
{
    [Test]
    public async Task ExecuteAsync_UsesSelectedConnectionAndTypedMethod()
    {
        var clients = new HubClients();
        var transport = new SignalRHostContainerTransport(new Context(clients));
        var request = new HostContainerExecutionRequest { Operation = HostContainerOperationKinds.ResolveImage, RequestedImage = "python:3.13-slim" };

        var result = await transport.ExecuteAsync("infra-connection", request, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(clients.ConnectionId, Is.EqualTo("infra-connection"));
        Assert.That(clients.Proxy.Method, Is.EqualTo(AutomatonHubMethods.ExecuteHostContainer));
        Assert.That(clients.Proxy.Request, Is.SameAs(request));
    }

    private sealed class Context(HubClients clients) : IHubContext<AutomatonHub>
    {
        public IHubClients Clients { get; } = clients;
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class HubClients : IHubClients
    {
        public string? ConnectionId { get; private set; }
        public Proxy Proxy { get; } = new();
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public ISingleClientProxy Client(string connectionId) { ConnectionId = connectionId; return Proxy; }
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Client(connectionId);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class Proxy : ISingleClientProxy
    {
        public string? Method { get; private set; }
        public HostContainerExecutionRequest? Request { get; private set; }
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Method = method;
            Request = (HostContainerExecutionRequest)args[0]!;
            return Task.FromResult((T)(object)new HostContainerExecutionResponse { Success = true, ResolvedImage = "python@sha256:test" });
        }
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
