using Automaton.Core.Enums;
using Automaton.Studio.Server.Controllers.Admin;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class AdminDashboardControllerTests
{
    [Test]
    public async Task GetSummary_ReturnsOperationalOverviewMetrics()
    {
        await using var dbContext = CreateDbContext();

        var now = DateTime.UtcNow;
        var monthStartUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        dbContext.Users.AddRange(
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "existing@example.com",
                NormalizedUserName = "EXISTING@EXAMPLE.COM",
                Email = "existing@example.com",
                NormalizedEmail = "EXISTING@EXAMPLE.COM",
                Name = "Existing User",
                DisplayName = "Existing User",
                PublicUsername = "existing-user",
                RegisteredAtUtc = monthStartUtc.AddDays(-3),
                RegistrationProvider = "password"
            },
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "new@example.com",
                NormalizedUserName = "NEW@EXAMPLE.COM",
                Email = "new@example.com",
                NormalizedEmail = "NEW@EXAMPLE.COM",
                Name = "New User",
                DisplayName = "New User",
                PublicUsername = "new-user",
                RegisteredAtUtc = monthStartUtc.AddDays(1),
                RegistrationProvider = "github"
            });

        dbContext.WebApps.AddRange(
            new WebApp
            {
                Id = Guid.NewGuid(),
                Name = "Published App",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                IsPublished = true,
                Created = now,
                Updated = now
            },
            new WebApp
            {
                Id = Guid.NewGuid(),
                Name = "Draft App",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                IsPublished = false,
                Created = now,
                Updated = now
            });

        dbContext.HostedRunners.AddRange(
            new HostedRunner
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                SubscriptionPlanId = Guid.NewGuid(),
                Name = "Runner 1",
                Status = HostedRunnerStatuses.Running,
                CpuCores = 2,
                MemoryMb = 2048,
                DiskMb = 20480,
                MaxApps = 2,
                ContainerName = "runner-1"
            },
            new HostedRunner
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                SubscriptionPlanId = Guid.NewGuid(),
                Name = "Runner 2",
                Status = HostedRunnerStatuses.Failed,
                CpuCores = 2,
                MemoryMb = 2048,
                DiskMb = 20480,
                MaxApps = 2,
                ContainerName = "runner-2"
            },
            new HostedRunner
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                SubscriptionPlanId = Guid.NewGuid(),
                Name = "Runner 3",
                Status = HostedRunnerStatuses.Running,
                CpuCores = 2,
                MemoryMb = 2048,
                DiskMb = 20480,
                MaxApps = 2,
                ContainerName = "runner-3",
                DeletedAt = now
            });

        dbContext.FlowExecutions.AddRange(
            new Automaton.Studio.Server.Entities.FlowExecution
            {
                Id = Guid.NewGuid(),
                FlowId = Guid.NewGuid(),
                Started = now.AddHours(-1),
                Finished = now,
                Status = WorkflowStatus.Success.ToString(),
                Application = "app-1"
            },
            new Automaton.Studio.Server.Entities.FlowExecution
            {
                Id = Guid.NewGuid(),
                FlowId = Guid.NewGuid(),
                Started = now.AddHours(-2),
                Finished = now,
                Status = WorkflowStatus.Error.ToString(),
                Application = "app-2"
            },
            new Automaton.Studio.Server.Entities.FlowExecution
            {
                Id = Guid.NewGuid(),
                FlowId = Guid.NewGuid(),
                Started = now.Date.AddDays(-1).AddHours(23),
                Finished = now.Date.AddDays(-1).AddHours(23).AddMinutes(5),
                Status = WorkflowStatus.Error.ToString(),
                Application = "older-app"
            });

        await dbContext.SaveChangesAsync();

        var controller = new AdminDashboardController(dbContext);

        var actionResult = await controller.GetSummary(CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var summary = okResult!.Value as AdminDashboardSummaryResponse;
        Assert.That(summary, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(summary!.TotalUsers, Is.EqualTo(2));
            Assert.That(summary.NewRegistrations, Is.EqualTo(1));
            Assert.That(summary.PublishedApps, Is.EqualTo(1));
            Assert.That(summary.ActiveRunners, Is.EqualTo(1));
            Assert.That(summary.ExecutionsToday, Is.EqualTo(2));
            Assert.That(summary.FailedExecutions, Is.EqualTo(1));
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
