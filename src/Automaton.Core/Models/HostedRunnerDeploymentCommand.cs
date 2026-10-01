namespace Automaton.Core.Models;

/// <summary>Durable, Runner-addressed request to deploy one staged app package.</summary>
public sealed record HostedRunnerDeploymentCommand(
    Guid OperationId,
    Guid ExecutionAttemptId,
    Guid RunnerId,
    Guid AppId,
    Guid DeploymentTargetId,
    string PackageUrl,
    long PackageLength,
    string PackageSha256,
    bool Clean,
    DateTime CreatedAtUtc);

public sealed record HostedRunnerBrokerConfiguration(
    string Host,
    int Port,
    string VirtualHost,
    string Username,
    string Password,
    string QueueName,
    bool TlsEnabled = false,
    string? TlsServerName = null,
    string? ManagementQueueName = null);

public sealed record HostedRunnerDeploymentResultRequest(
    Guid ExecutionAttemptId,
    DeployAppPackageResult Result);

public sealed record HostedRunnerDeploymentCommandRejectionRequest(
    Guid ExecutionAttemptId,
    string Reason);
