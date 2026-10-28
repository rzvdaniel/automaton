using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Transfers a deployment package through the Runner's durable execution queue.</summary>
public sealed class DeploymentPackageRunnerService(
    IRunnerFileTransferGateway transfers,
    IOptions<MachineContainerResourceOptions>? machineResources = null)
{
    public async Task<DeployAppPackageResult> DeployAsync(
        Guid appId,
        Guid ownerId,
        Guid deploymentTargetId,
        Runner runner,
        IFormFile file,
        ApplicationContract packageApplication,
        bool clean,
        Guid? deploymentOperationId,
        Guid? executionAttemptId,
        CancellationToken cancellationToken)
    {
        RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(runner);
        if (packageApplication.Runtime.Command is not null || packageApplication.Build.Command is not null)
            RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(runner);

        var operationId = CreateOperationId(deploymentOperationId, executionAttemptId, runner.Id);
        var isMachineManagement = RunnerApplicationProfiles.Normalize(runner.ApplicationProfile)
            == RunnerApplicationProfiles.MachineManagement;
        var actionResources = isMachineManagement ? machineResources?.Value : null;
        if (isMachineManagement && actionResources?.IsValid() != true)
            throw new InvalidOperationException("Machine Action preparation is paused because valid bounded container settings are unavailable.");

        var command = new RunnerFileTransferCommand(
            RunnerFileTransferKinds.DeployPackage,
            appId,
            operationId.ToString("N"),
            FileName: $"{appId:N}.zip",
            ContentType: "application/zip",
            DeploymentTargetId: deploymentTargetId,
            Clean: clean,
            DeploymentOperationId: deploymentOperationId,
            ExecutionAttemptId: executionAttemptId,
            ActionContainerPolicy: actionResources?.ToActionContainerPolicy());

        var (_, result) = await transfers.ExecuteAsync(operationId, runner.Id, ownerId,
            command, file, cancellationToken);
        if (result.FileTransferResult?.Deployment is { } deployment)
            return deployment;
        throw new InvalidOperationException(result.Error
            ?? result.FileTransferResult?.Error
            ?? "Runner deployment did not complete successfully.");
    }

    private static Guid CreateOperationId(Guid? deploymentOperationId, Guid? executionAttemptId, Guid runnerId)
    {
        if (!deploymentOperationId.HasValue || !executionAttemptId.HasValue)
            return Guid.NewGuid();
        var identity = $"deploy-package:{deploymentOperationId.Value:N}:{executionAttemptId.Value:N}:{runnerId:N}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity))[..16]);
    }
}
