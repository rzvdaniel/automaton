using Automaton.Api.Enums;

namespace Automaton.Api.Models;

public sealed record AppDeploymentTargetRunnerSummary(
    Guid Id,
    string Name,
    string Purpose,
    string? ApplicationProfile,
    bool IsHostedRunner,
    bool IsOnline);

/// <param name="RoutePath">Path relative to app-{AppId:N}.{configured apps domain}. The primary target uses /.</param>
public sealed record AppDeploymentTargetSummary(
    Guid Id,
    Guid AppId,
    string Name,
    string Slug,
    bool IsDefault,
    DeploymentTargetStatus Status,
    bool GenericRuntimeDeployed,
    DateTimeOffset? LastDeploymentAt,
    DateTimeOffset? LastSuccessfulDeploymentAt,
    bool CleanupPending,
    string? LastCleanupError,
    DateTimeOffset? LastCleanupAttemptAt,
    AppDeploymentTargetRunnerSummary? Runner,
    string RoutePath,
    string? LastDeploymentError = null);

public sealed class CreateAppDeploymentTargetRequest
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public Guid? RunnerId { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class UpdateAppDeploymentTargetRequest
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
}

public sealed class AssignAppDeploymentTargetRunnerRequest
{
    public Guid? RunnerId { get; init; }
    public bool CleanupDeployedRuntime { get; init; }
}
