using System.Security.Claims;
using System.Text.RegularExpressions;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class SelfHostedRunnerInstallationServiceTests
{
    [Test]
    public async Task Create_StoresOnlyHashAndBuildsIsolatedStandardCommand()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateService(db, userId);

        var response = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "standard-self-hosted",
            ApplicationProfile = "standard"
        }, CancellationToken.None);

        var enrollment = await db.SelfHostedRunnerEnrollments.SingleAsync();
        Assert.That(response.ApplicationProfile, Is.EqualTo(RunnerApplicationProfiles.Standard));
        Assert.That(response.InstallationCommand, Does.Contain("automaton/runner"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("--privileged"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("/var/run/docker.sock"));
        Assert.That(enrollment.EnrollmentTokenHash, Does.Not.Contain("AUTOMATON_ENROLLMENT_TOKEN"));
        Assert.That(enrollment.EnrollmentTokenHash, Is.Not.EqualTo(string.Empty));
        Assert.That(enrollment.UserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task PrivilegedInstallation_UsesHostArgumentsButRemainsApplicationRunner()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, Guid.NewGuid());

        var response = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "privileged-self-hosted",
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
        }, CancellationToken.None);

        Assert.That(response.RequiresHostAccessAcknowledgement, Is.True);
        Assert.That(response.HostAccessWarning, Does.Contain("host files"));
        Assert.That(response.InstallationCommand, Does.Contain("--privileged"));
        Assert.That(response.InstallationCommand, Does.Contain("--pid host"));
        Assert.That(response.InstallationCommand, Does.Contain("/var/run/docker.sock:/var/run/docker.sock"));
        Assert.That(response.InstallationCommand, Does.Contain("-v /:/host"));
        Assert.That(response.InstallationCommand, Does.Contain("AUTOMATON_RUNNER_PURPOSE='Managed'"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("RUNNER_HOST_ID"));
        Assert.That(response.InstallationCommand, Does.Not.Contain("HOSTED_RUNNER_ID"));
    }

    [TestCase("unknown")]
    [TestCase("Infrastructure")]
    public async Task Create_RejectsNonApplicationProfiles(string profile)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, Guid.NewGuid());

        Assert.That(
            async () => await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
            {
                RunnerName = "invalid",
                ApplicationProfile = profile
            }, CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task Exchange_AssignsEnrollmentProfileAndNormalCapabilities_AndCannotBeReplayed()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateService(db, userId);
        var installation = await service.CreateAsync(new CreateSelfHostedRunnerInstallationRequest
        {
            RunnerName = "registered-self-hosted",
            ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost
        }, CancellationToken.None);
        var token = Regex.Match(installation.InstallationCommand, "AUTOMATON_ENROLLMENT_TOKEN='([^']+)'").Groups[1].Value;

        var response = await service.ExchangeAsync(new RunnerEnrollmentExchangeRequest
        {
            EnrollmentToken = token,
            RunnerName = "registered-self-hosted",
            RunnerVersion = "test",
            MachineName = "user-host"
        }, CancellationToken.None);

        Assert.That(response, Is.Not.Null);
        var runner = await db.Runners.SingleAsync();
        Assert.That(runner.Purpose, Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(runner.ApplicationProfile, Is.EqualTo(RunnerApplicationProfiles.PrivilegedHost));
        Assert.That(runner.CapabilitiesJson, Does.Contain(RunnerCapabilities.ApplicationHosting));
        Assert.That(runner.CapabilitiesJson, Does.Contain(RunnerCapabilities.WorkflowExecution));
        Assert.That(await db.HostedRunners.CountAsync(), Is.EqualTo(0));
        Assert.That(await db.RunnerHosts.CountAsync(), Is.EqualTo(0));
        Assert.That(await db.RunnerUsers.AnyAsync(x => x.RunnerId == runner.Id && x.UserId == userId), Is.True);
        Assert.That(response!.ApiKey, Does.StartWith("ak_runner_"));
        Assert.That(await service.ExchangeAsync(new RunnerEnrollmentExchangeRequest { EnrollmentToken = token }, CancellationToken.None), Is.Null);
    }

    private static SelfHostedRunnerInstallationService CreateService(ApplicationDbContext db, Guid userId)
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test");
        context.User = new ClaimsPrincipal(identity);
        var userContext = new UserContextService(new HttpContextAccessor { HttpContext = context });
        var audit = new RecordingAuditEventService();
        var apiKeys = new ApiKeyService(db, NullLogger<ApiKeyService>.Instance, null!, audit);
        return new SelfHostedRunnerInstallationService(
            db, apiKeys, userContext, audit,
            Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
            Options.Create(new HostedRunnerOptions { DockerImage = "automaton/runner:test", ServerUrl = "https://api.example.test" }),
            NullLogger<SelfHostedRunnerInstallationService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"self-hosted-{Guid.NewGuid()}").Options);

    private sealed class RecordingAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
