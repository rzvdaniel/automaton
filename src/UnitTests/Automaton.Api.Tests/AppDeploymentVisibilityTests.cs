using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Automaton.Api.Tests;

[TestFixture]
public class AppDeploymentVisibilityTests
{

    [Test]
    public async Task ListAsync_MapsTargetLocalGenericRuntimeDeploymentState()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var updatedAtUtc = DateTime.UtcNow.AddDays(-2);
        var genericPromotionAtUtc = DateTime.UtcNow.AddHours(-3);
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "metadata-app");
        dbContext.WebApps.Single().Updated = updatedAtUtc;
        AppAuthorizationDeploymentTestData.SetDefaultDeploymentTarget(
            dbContext,
            appId,
            runnerId: null,
            genericRuntimeDeployed: true,
            lastSuccessfulDeploymentAtUtc: genericPromotionAtUtc);

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path, out _);

        await AppAuthorizationDeploymentTestData.AssertGenericRuntimeListingAsync(service, updatedAtUtc, genericPromotionAtUtc);

        var legacyCompletionAtUtc = DateTime.UtcNow.AddHours(-1);
        AppAuthorizationDeploymentTestData.SetDefaultDeploymentTarget(
            dbContext,
            appId,
            runnerId: null,
            genericRuntimeDeployed: true,
            lastSuccessfulDeploymentAtUtc: legacyCompletionAtUtc);

        await AppAuthorizationDeploymentTestData.AssertGenericRuntimeListingAsync(service, updatedAtUtc, legacyCompletionAtUtc);
    }


    [Test]
    public async Task ListAsync_LegacyStorageMetadataCannotManufactureGenericRuntimeAvailability()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "legacy-metadata-app");
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path, out var storage);
        await storage.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
        {
            metadata.RuntimeAvailable = true;
        });

        var app = (await service.ListAsync(cancellationToken: CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(app.Runtime.GenericRuntimeDeployed, Is.False);
            Assert.That(app.LiveEnvironment!.GenericRuntimeDeployed, Is.False);
        });
    }


    [Test]
    public async Task ListAsync_IsolatesCorruptStorageMetadata()
    {
        var ownerId = Guid.NewGuid();
        var healthyAppId = Guid.NewGuid();
        var corruptAppId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, healthyAppId, ownerId, "healthy");
        AppAuthorizationTestData.SeedApp(dbContext, corruptAppId, ownerId, "corrupt");
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path, out _);
        await AppAuthorizationDeploymentTestData.WriteCorruptAppStorageMetadataAsync(tempRoot.Path, ownerId, corruptAppId);

        var apps = await service.ListAsync(cancellationToken: CancellationToken.None);

        Assert.That(apps, Has.Count.EqualTo(2));
        var corrupt = apps.Single(app => app.Id == corruptAppId);
        Assert.That(corrupt.Runtime.GenericRuntimeDeployed, Is.False);
    }

    [Test]
    public async Task GetAccessibleAppInfoByIdAsync_ReturnsNullForUnauthorizedApp()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "private-app");

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, callerId);

        var result = await service.GetAccessibleAppInfoByIdAsync(appId, CancellationToken.None);

        Assert.That(result, Is.Null);
    }


    [Test]
    public async Task GetAccessibleAppInfoByIdAsync_ReturnsAppForOwner()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId);

        var result = await service.GetAccessibleAppInfoByIdAsync(appId, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(appId));
        Assert.That(result.OwnerId, Is.EqualTo(ownerId));
    }
}
