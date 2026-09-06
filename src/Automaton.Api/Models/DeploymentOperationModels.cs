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

public sealed class DeployPackageResponse
{
    public Guid DeploymentTargetId { get; init; }
    public string TargetSlug { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string State { get; init; } = AppDeployStates.Ready;
    public bool RequiresRetry { get; init; }
    public int RunnerCount { get; init; }
    public int SourceFilesDeployed { get; init; }
    public int RuntimeFilesDeployed { get; init; }
    public string? SnapshotPath { get; init; }
    public string? Message { get; init; }
    public List<string>? Logs { get; init; }
    public List<Automaton.Core.Models.AppActionDeploymentResult> Actions { get; init; } = [];
}
