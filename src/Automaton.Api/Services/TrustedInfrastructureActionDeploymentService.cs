using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public sealed class TrustedInfrastructureActionDeploymentService(
    AppActionRunnerGateway gateway,
    ApplicationDbContext db)
    : ITrustedInfrastructureActionDeploymentService
{
    public async Task<AppActionModuleInfo> EnsureDeployedAsync(
        Guid runnerId,
        string actionKey,
        CancellationToken cancellationToken)
    {
        var deploymentTargetId = await GetPlatformDeploymentTargetIdAsync(cancellationToken);
        return await EnsureDeployedOnTargetAsync(runnerId, deploymentTargetId, actionKey, cancellationToken);
    }

    public async Task<AppActionModuleInfo> EnsureDeployedOnTargetAsync(
        Guid runnerId,
        Guid deploymentTargetId,
        string actionKey,
        CancellationToken cancellationToken)
    {
        var (actionRoot, expectedHash) = GetBundledAction(actionKey);
        var current = await GetDeployedActionAsync(runnerId, deploymentTargetId, actionKey, cancellationToken);
        if (IsCurrentAction(current, expectedHash))
            return current!;

        await DeployActionAsync(
            runnerId,
            deploymentTargetId,
            actionKey,
            actionRoot,
            expectedHash,
            cancellationToken);

        return await GetDeployedActionAsync(runnerId, deploymentTargetId, actionKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Trusted infrastructure Action '{actionKey}' was not visible after deployment.");
    }

    private static (string ActionRoot, string ExpectedHash) GetBundledAction(string actionKey)
    {
        TrustedInfrastructureActions.EnsureAllowed(actionKey);
        var actionRoot = Path.Combine(
            AppContext.BaseDirectory,
            "SystemApps",
            "automaton-infrastructure",
            "actions",
            actionKey);
        var manifestPath = Path.Combine(actionRoot, "action.yaml");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Bundled trusted infrastructure Action '{actionKey}' was not found.");
        _ = AppActionManifestParser.ParseFile(manifestPath, actionKey, allowPrivilegedCapabilities: true);

        var expectedHash = ComputeContentHash(actionRoot);
        return (actionRoot, expectedHash);
    }

    private Task<Guid> GetPlatformDeploymentTargetIdAsync(CancellationToken cancellationToken)
        => db.AppDeploymentTargets
            .Where(target =>
                target.WebAppId == InfrastructureAppTrust.PlatformAppId &&
                target.IsDefault)
            .Select(target => target.Id)
            .SingleAsync(cancellationToken);

    private async Task<AppActionModuleInfo?> GetDeployedActionAsync(
        Guid runnerId,
        Guid deploymentTargetId,
        string actionKey,
        CancellationToken cancellationToken)
    {
        var modules = await gateway.ListModulesAsync(runnerId,
            InfrastructureAppTrust.PlatformAppId, deploymentTargetId, actionKey, cancellationToken);
        return modules.Actions.SingleOrDefault(x => string.Equals(x.Key, actionKey, StringComparison.Ordinal));
    }

    private static bool IsCurrentAction(AppActionModuleInfo? action, string expectedHash)
        => action is not null && string.Equals(action.ContentHash, expectedHash, StringComparison.Ordinal);

    private async Task DeployActionAsync(
        Guid runnerId,
        Guid deploymentTargetId,
        string actionKey,
        string actionRoot,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var response = await gateway.SendAsync<AppActionDeploymentResult>(runnerId, "prepare-module",
            new PrepareAppActionModuleRequest(InfrastructureAppTrust.PlatformAppId, deploymentTargetId,
                actionKey, CreateSourceZip(actionRoot), expectedHash, SystemInfrastructure: true), cancellationToken);
        if (response.Status is not ("deployed" or "unchanged"))
            throw new InvalidOperationException(response.Message ?? $"Trusted infrastructure Action '{actionKey}' could not be deployed.");
    }

    private static byte[] CreateSourceZip(string actionRoot)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in Directory.GetFiles(actionRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(actionRoot, path).Replace('\\', '/');
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var source = File.OpenRead(path);
                source.CopyTo(entryStream);
            }
        }
        return output.ToArray();
    }

    private static string ComputeContentHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(path));
        }
        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }
}
