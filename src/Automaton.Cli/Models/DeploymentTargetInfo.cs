namespace Automaton.Cli.Models;

public sealed record DeploymentTargetRunnerInfo(
    string Id,
    string Name,
    bool IsOnline,
    bool IsHostedRunner = false,
    string? ApplicationProfile = null);

public sealed record DeploymentTargetInfo(
    string Id,
    string Name,
    string Slug,
    bool IsDefault,
    string Status,
    bool LegacyControllerRuntimeDeployed,
    DateTimeOffset? LastDeploymentAt,
    DeploymentTargetRunnerInfo? Runner,
    bool PrivilegedHostAccessApproved = false,
    DateTimeOffset? PrivilegedHostAccessApprovedAtUtc = null,
    string? PrivilegedHostAccessApprovedByUserId = null);
