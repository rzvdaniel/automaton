using Automaton.Api.Data;
using Automaton.Api.Services;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class AppAuthorizationDeploymentTestData
{
    public static void SetDefaultDeploymentTarget(
        ApplicationDbContext dbContext,
        Guid appId,
        Guid? runnerId,
        bool genericRuntimeDeployed,
        DateTime? lastSuccessfulDeploymentAtUtc = null)
    {
        var target = dbContext.AppDeploymentTargets.Single(item => item.WebAppId == appId && item.IsDefault);
        target.ExecutionRunnerId = runnerId;
        target.GenericRuntimeDeployed = genericRuntimeDeployed;
        target.LastSuccessfulDeploymentAt = lastSuccessfulDeploymentAtUtc;
        dbContext.SaveChanges();
    }

    public static AppDeploymentTargetState ReadDefaultDeploymentTarget(ApplicationDbContext dbContext, Guid appId)
    {
        var target = dbContext.AppDeploymentTargets.Single(item => item.WebAppId == appId && item.IsDefault);
        return new AppDeploymentTargetState(target.ExecutionRunnerId, target.GenericRuntimeDeployed);
    }

    public static async Task WriteCorruptAppStorageMetadataAsync(string contentRootPath, Guid userId, Guid appId)
    {
        var metadataPath = Path.Combine(contentRootPath, "AppData", "apps", userId.ToString(), appId.ToString(), "_metadata.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, "{invalid");
    }

    public static async Task AssertGenericRuntimeListingAsync(
        AppsService service,
        DateTime expectedUpdatedAtUtc,
        DateTime expectedLastDeploymentAtUtc)
    {
        var app = (await service.ListAsync(cancellationToken: CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(app.UpdatedAtUtc, Is.EqualTo(expectedUpdatedAtUtc));
            Assert.That(app.Runtime.GenericRuntimeDeployed, Is.True);
            Assert.That(app.LiveEnvironment!.GenericRuntimeDeployed, Is.True);
            Assert.That(app.LiveEnvironment.RoutePath, Is.EqualTo("/"));
            Assert.That(app.LiveEnvironment.LastSuccessfulDeploymentAtUtc, Is.EqualTo(expectedLastDeploymentAtUtc));
        });
    }
}

internal sealed record AppDeploymentTargetState(Guid? ExecutionRunnerId, bool GenericRuntimeDeployed);
