using Automaton.Api.Entities;

namespace Automaton.Api.Models;

public sealed class DeploymentOperationResponse
{
    public Guid OperationId { get; init; }
    public string Status { get; init; } = DeploymentOperationStatuses.Pending;
    public bool? Success { get; init; }
    public string? Error { get; init; }
    public DeployPackageResponse? Result { get; init; }
}
