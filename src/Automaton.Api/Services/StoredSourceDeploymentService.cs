using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

/// <summary>
/// Deploys an app from the source the server stores for it (its last deploy, or a fork's copy),
/// using the same package format and operation pipeline as a CLI deploy.
/// </summary>
public sealed class StoredSourceDeploymentService(
    ResourceAuthorizationService authorizationService,
    AppFileStorageService fileStorageService,
    DeploymentOperationService deploymentOperationService)
{
    /// <exception cref="KeyNotFoundException">The app does not exist for this user.</exception>
    /// <exception cref="UnauthorizedAccessException">The user cannot update the app.</exception>
    /// <exception cref="InvalidDataException">The app has no deployable stored source.</exception>
    public async Task<DeploymentOperationResponse> AcceptAsync(
        Guid appId, Guid userId, Guid ownerId, string? targetSlug, bool clean, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanAccessAppAsync(appId, userId, cancellationToken))
            throw new KeyNotFoundException("App not found.");
        if (!await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to deploy this app.");

        await using var package = CreatePackage(appId, fileStorageService.GetAppSourceDirectoryPath(ownerId, appId));
        var file = new FormFile(package, 0, package.Length, "file", "deploy-package.zip");
        return await deploymentOperationService.AcceptAsync(appId, userId, targetSlug, file, clean, cancellationToken);
    }

    internal static Stream CreatePackage(Guid appId, string sourceDirectory)
    {
        var configPath = Path.Combine(sourceDirectory, "automaton.json");
        if (!File.Exists(configPath))
            throw new InvalidDataException("This app has no stored source to deploy. Deploy it once with the CLI first.");

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;
        var application = root.TryGetProperty("application", out var applicationElement)
            ? ApplicationContractSerialization.Deserialize(applicationElement.GetRawText())
            : ApplicationContractDefaults.Create();
        var scope = root.TryGetProperty("source", out var scopeElement)
            ? scopeElement.Deserialize<DeployScopeConfig>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            : null;

        if (application.Runtime.Command is not null && application.Build.Command is null)
            throw new InvalidDataException(
                "This app deploys a prebuilt artifact that the server does not store. Deploy it with the CLI.");

        return ZipService.CreateDeployPackageZip(sourceDirectory, appId.ToString(), application, scope);
    }
}
