using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

public sealed class TrustedInfrastructureActionDeploymentService(IHubContext<AutomatonHub> hub)
{
    private static readonly IReadOnlySet<string> AllowedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "provision-hosted-runner",
        "reprovision-hosted-runner",
        "restart-hosted-runner",
        "delete-hosted-runner"
    };

    public async Task<AppActionModuleInfo> EnsureDeployedAsync(
        string connectionId,
        string actionKey,
        CancellationToken cancellationToken)
    {
        if (!AllowedActions.Contains(actionKey))
            throw new UnauthorizedAccessException("The requested trusted infrastructure Action is not allowlisted.");

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
        var modules = await hub.Clients.Client(connectionId)
            .InvokeAsync<AppActionModulesResponse>(AutomatonHubMethods.ListAppActionModules, InfrastructureAppTrust.PlatformAppId, cancellationToken);
        var current = modules.Actions.SingleOrDefault(x => string.Equals(x.Key, actionKey, StringComparison.Ordinal));
        if (current != null && string.Equals(current.ContentHash, expectedHash, StringComparison.Ordinal)) return current;

        var response = await hub.Clients.Client(connectionId)
            .InvokeAsync<AppActionDeploymentResult>(
                AutomatonHubMethods.PrepareAppActionModule,
                new PrepareAppActionModuleRequest(
                    InfrastructureAppTrust.PlatformAppId,
                    actionKey,
                    CreateSourceZip(actionRoot),
                    expectedHash,
                    SystemInfrastructure: true),
                cancellationToken);
        if (response.Status is not ("deployed" or "unchanged"))
            throw new InvalidOperationException(response.Message ?? $"Trusted infrastructure Action '{actionKey}' could not be deployed.");

        modules = await hub.Clients.Client(connectionId)
            .InvokeAsync<AppActionModulesResponse>(AutomatonHubMethods.ListAppActionModules, InfrastructureAppTrust.PlatformAppId, cancellationToken);
        return modules.Actions.SingleOrDefault(x => string.Equals(x.Key, actionKey, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Trusted infrastructure Action '{actionKey}' was not visible after deployment.");
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
