using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

public interface IHostContainerTransport
{
    Task<HostContainerExecutionResponse> ExecuteAsync(
        string connectionId,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed class SignalRHostContainerTransport(IHubContext<AutomatonHub> hub) : IHostContainerTransport
{
    public Task<HostContainerExecutionResponse> ExecuteAsync(
        string connectionId,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<HostContainerExecutionResponse>(
            AutomatonHubMethods.ExecuteHostContainer, request, cancellationToken);
}
