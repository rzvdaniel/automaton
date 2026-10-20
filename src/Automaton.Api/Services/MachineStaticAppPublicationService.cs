using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Api.Services;

/// <summary>Coordinates static output publication with its assigned Machine Management Runner.</summary>
public sealed class MachineStaticAppPublicationService(
    ApplicationDbContext dataContext,
    IRunnerFileTransferGateway transfers)
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<MachineStaticAppPublicationService>();

    public async Task<bool> PublishIfAssignedAsync(
        WebApp app,
        string runtimeVisibility,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (runtimeVisibility != AppRuntimeVisibilities.Public) return false;
        var target = await FindMachineTargetAsync(app.Id, cancellationToken);
        if (target is null) return false;

        await PersistAssignmentBeforeDispatchAsync(cancellationToken);
        var outputDirectory = ReadDeclaredOutputDirectory(app.ApplicationContractJson);
        var operationId = Guid.NewGuid();
        var command = new RunnerFileTransferCommand(
            RunnerFileTransferKinds.PublishStaticSite,
            app.Id,
            operationId.ToString("N"),
            RelativePath: outputDirectory,
            DeploymentTargetId: target.Id,
            DeploymentOperationId: operationId);
        var (_, result) = await transfers.ExecuteAsync(
            operationId, target.RunnerId, ownerId, command, input: null, cancellationToken, TimeSpan.FromMinutes(15));
        if (result.FileTransferResult is not { Success: true })
            throw new InvalidOperationException(
                result.FileTransferResult?.Error ?? result.Error ?? "The Machine Management Runner rejected static publishing.");
        return true;
    }

    public async Task QueueRemovalIfAssignedAsync(
        Guid appId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var target = await FindMachineTargetAsync(appId, cancellationToken);
        if (target is null) return;
        var operationId = Guid.NewGuid();
        try
        {
            await transfers.QueueAsync(operationId, target.RunnerId, ownerId,
                new RunnerFileTransferCommand(
                    RunnerFileTransferKinds.UnpublishStaticSite,
                    appId,
                    operationId.ToString("N"),
                    DeploymentTargetId: target.Id,
                    DeploymentOperationId: operationId),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception,
                "Could not queue static-site cleanup for unpublished app {AppId}; public access has already been revoked",
                appId);
        }
    }

    private Task<MachineTarget?> FindMachineTargetAsync(Guid appId, CancellationToken cancellationToken)
        => dataContext.AppDeploymentTargets
            .AsNoTracking()
            .Where(target => target.WebAppId == appId && target.IsDefault
                && target.ExecutionRunnerId != null
                && target.ExecutionRunner!.Purpose == RunnerPurposes.Managed
                && target.ExecutionRunner.ApplicationProfile == RunnerApplicationProfiles.MachineManagement)
            .Select(target => new MachineTarget(target.Id, target.ExecutionRunnerId!.Value))
            .SingleOrDefaultAsync(cancellationToken);

    private Task PersistAssignmentBeforeDispatchAsync(CancellationToken cancellationToken)
        => dataContext.SaveChangesAsync(cancellationToken);

    private static string ReadDeclaredOutputDirectory(string contractJson)
    {
        var contract = ApplicationContractSerialization.Deserialize(contractJson);
        if (contract.Build.Command is null || string.IsNullOrWhiteSpace(contract.Build.OutputDirectory))
            throw new InvalidOperationException(
                "Machine Management Runner static publishing requires a build command and a declared build.outputDirectory.");
        return contract.Build.OutputDirectory;
    }

    private sealed record MachineTarget(Guid Id, Guid RunnerId);
}
