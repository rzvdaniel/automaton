using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppStatusReadTests
{
    [Test]
    public async Task ListsTheLastActionRunAndTheNextOneWithoutSecretsOrSettings()
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        var old = new DateTime(2026, 11, 14, 4, 30, 0, DateTimeKind.Utc);
        var next = new DateTime(2026, 11, 16, 4, 30, 0, DateTimeKind.Utc);
        fixture.Db.AppModuleExecutions.Add(Run(fixture.App.Id, "offsite", "Succeeded", old));
        fixture.Db.AppModuleExecutions.Add(Run(fixture.App.Id, "offsite", "Failed", old.AddDays(1)));
        fixture.Db.ActionSchedules.Add(new ActionSchedule
        {
            Id = Guid.NewGuid(), OwnerUserId = fixture.UserId, ApplicationId = fixture.App.Id, ActionKey = "offsite",
            Name = "Nightly", IntervalSeconds = 300, Enabled = true, NextRunAtUtc = next
        });
        await fixture.Db.SaveChangesAsync();

        var item = (await new AppStatusReadService(fixture.Db).ListAsync(default)).Single(i => i.Id == fixture.App.Id);

        Assert.That(item.LastActionRun!.Status, Is.EqualTo("Failed"));
        Assert.That(item.LastActionRun.ModuleKey, Is.EqualTo("offsite"));
        Assert.That(item.NextActionRunAtUtc, Is.EqualTo(next));
        var names = typeof(AppStatusItem).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToArray();
        Assert.That(names, Has.None.Contain("secret").And.None.Contain("source").And.None.Contain("log").And.None.Contain("setting"));
    }

    [Test]
    public async Task AFailedLiveDeployReportsOnlyTheFirstLineOfItsError()
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        var target = fixture.Db.AppDeploymentTargets.Single(t => t.WebAppId == fixture.App.Id && t.IsDefault);
        target.Status = DeploymentTargetStatus.Failed;
        fixture.Db.DeploymentOperations.Add(new DeploymentOperation
        {
            Id = Guid.NewGuid(), WebAppId = fixture.App.Id, OwnerUserId = fixture.UserId, TargetSlug = target.Slug, PackagePath = "p",
            Status = DeploymentOperationStatuses.Failed, ErrorMessage = "Build failed\nTOKEN=abc /srv/path trace", CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var item = (await new AppStatusReadService(fixture.Db).ListAsync(default)).Single(i => i.Id == fixture.App.Id);

        Assert.That(item.LiveStatus, Is.EqualTo("Failed"));
        Assert.That(item.LastDeploymentError, Is.EqualTo("Build failed"));
    }

    [Test]
    public void ALongErrorIsCutAndAnEmptyOneIsNull()
    {
        Assert.That(AppStatusReadService.Summarize(new string('x', 500))!.Length, Is.EqualTo(201));
        Assert.That(AppStatusReadService.Summarize("  "), Is.Null);
    }

    [Test]
    public async Task OnlyAnApprovedAppOrAnAdministratorPassesThePolicy()
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization(options =>
            AppPermissionAuthenticationOptions.AddPermissionPolicy(options, "p", AppPermissionApproval.AppsReadStatus)).BuildServiceProvider();
        var policy = (await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync("p"))!;
        var service = services.GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

        Assert.Multiple(async () =>
        {
            Assert.That((await service.AuthorizeAsync(User(new Claim("app_id", Guid.NewGuid().ToString()), new Claim("app_permission", "apps:read-status")), null, policy)).Succeeded, Is.True, "approved app");
            Assert.That((await service.AuthorizeAsync(User(new Claim("app_id", Guid.NewGuid().ToString()), new Claim("app_permission", "backups:read")), null, policy)).Succeeded, Is.False, "app without approval");
            Assert.That((await service.AuthorizeAsync(User(new Claim(ClaimTypes.Role, UserRole.Administrator)), null, policy)).Succeeded, Is.False, "plain administrator");
            Assert.That((await service.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy)).Succeeded, Is.False, "anonymous");
        });
        Assert.That(typeof(AppStatusController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy,
            Is.EqualTo(AppPermissionAuthenticationOptions.AppsReadStatusPolicy));
    }

    private static AppModuleExecution Run(Guid appId, string key, string status, DateTime at)
        => new() { Id = Guid.NewGuid(), AppId = appId, ModuleType = "action", ModuleKey = key, TriggerType = "schedule", Status = status, CreatedAtUtc = at };
}
