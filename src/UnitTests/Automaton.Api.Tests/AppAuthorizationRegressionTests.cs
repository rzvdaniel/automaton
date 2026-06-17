using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppAuthorizationRegressionTests
{
    [Test]
    public async Task GetAccessibleAppInfoByIdAsync_ReturnsNullForUnauthorizedApp()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedApp(dbContext, appId, ownerId, "private-app");

        var service = CreateAppsService(dbContext, callerId);

        var result = await service.GetAccessibleAppInfoByIdAsync(appId, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAccessibleAppInfoByIdAsync_ReturnsAppForOwner()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedApp(dbContext, appId, ownerId, "owner-app");

        var service = CreateAppsService(dbContext, ownerId);

        var result = await service.GetAccessibleAppInfoByIdAsync(appId, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(appId));
        Assert.That(result.OwnerId, Is.EqualTo(ownerId));
    }

    [Test]
    public async Task GetLinkedStepsAsync_ReturnsOnlyStepsLinkedToApp()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var linkedStepId = Guid.NewGuid();
        var unrelatedStepId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();

        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Test App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = ownerId
        });

        dbContext.CustomSteps.AddRange(
            CreateStep(linkedStepId, "linked-step"),
            CreateStep(unrelatedStepId, "unrelated-step"));

        dbContext.CustomStepUsers.AddRange(
            new CustomStepUser { CustomStepId = linkedStepId, UserId = ownerId },
            new CustomStepUser { CustomStepId = unrelatedStepId, UserId = ownerId });

        dbContext.WebAppSteps.Add(new WebAppStep
        {
            WebAppId = appId,
            CustomStepId = linkedStepId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = ownerId
        });

        await dbContext.SaveChangesAsync();

        var service = new AppsStepExecutionService(dbContext, null!, null!);

        var steps = (await service.GetLinkedStepsAsync(appId)).ToList();

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].Id, Is.EqualTo(linkedStepId));
        Assert.That(steps[0].Name, Is.EqualTo("linked-step"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AppsService CreateAppsService(ApplicationDbContext dbContext, Guid userId)
    {
        var userContextService = new UserContextService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "Test"))
            }
        });

        var authorizationService = new ResourceAuthorizationService(dbContext, userContextService);

        return new AppsService(
            dbContext,
            userContextService,
            null!,
            authorizationService,
            null!,
            null!,
            new StubWebHostEnvironment(),
            new NoOpAuditEventService());
    }

    private static void SeedApp(ApplicationDbContext dbContext, Guid appId, Guid ownerId, string slug)
    {
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "App",
            Summary = string.Empty,
            Description = string.Empty,
            PublicUrl = slug,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = ownerId
        });
        dbContext.SaveChanges();
    }

    private static Api.Entities.CustomStep CreateStep(Guid id, string name)
    {
        return new Api.Entities.CustomStep
        {
            Id = id,
            Name = name,
            DisplayName = name,
            Description = string.Empty,
            MoreInfo = string.Empty,
            Icon = "code",
            Category = "App Steps",
            Definition = "{\"language\":\"Python\",\"codeInputVariables\":[],\"codeOutputVariables\":[]}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(
            AuditEventWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
