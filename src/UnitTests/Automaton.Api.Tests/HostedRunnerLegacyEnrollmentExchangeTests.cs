using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class HostedRunnerLegacyEnrollmentExchangeTests
{
    [Test]
    public async Task LegacyHostedEnrollmentIsRejectedWithoutIssuingRunnerCredentials()
    {
        var outcome = await LegacyHostedEnrollmentFixture.ExchangeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Response, Is.Null);
            Assert.That(outcome.Status, Is.EqualTo(RunnerEnrollmentStatuses.Failed));
            Assert.That(outcome.FailureReason, Does.Contain("Management Runner"));
            Assert.That(outcome.RunnerCount, Is.EqualTo(1));
            Assert.That(outcome.LinkedRunnerId, Is.Null);
        });
    }
}

internal static class LegacyHostedEnrollmentFixture
{
    private const string Token = "legacy-hosted-enrollment-token";

    internal static async Task<LegacyEnrollmentOutcome> ExchangeAsync()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var service = TestRunnerEnrollmentFactory.Create(
            db,
            new InfrastructureWorkflowExecutionServiceTests.RecordingAuditEventService());
        var response = await service.ExchangeAsync(new RunnerEnrollmentExchangeRequest
        {
            EnrollmentToken = Token,
            RunnerName = seeded.HostedRunner.Name,
            RunnerVersion = "test",
            MachineName = "legacy-host"
        }, CancellationToken.None);

        return new LegacyEnrollmentOutcome(
            response,
            seeded.Enrollment.Status,
            seeded.Enrollment.FailureReason,
            await db.Runners.CountAsync(),
            (await db.HostedRunners.SingleAsync()).RunnerId);
    }

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<LegacyEnrollmentSeed> SeedAsync(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        var ownerId = Guid.NewGuid();
        var infrastructureRunner = CreateInfrastructureRunner();
        var host = CreateHost(infrastructureRunner.Id);
        var hostedRunner = CreateHostedRunner(ownerId, host.Id);
        var operation = CreateProvisioningOperation(hostedRunner.Id, now);
        var enrollment = CreateEnrollment(ownerId, host.Id, hostedRunner.Id, operation.Id, now);
        db.AddRange(infrastructureRunner, host, hostedRunner, operation, enrollment);
        await db.SaveChangesAsync();
        return new LegacyEnrollmentSeed(hostedRunner, enrollment);
    }

    private static Runner CreateInfrastructureRunner() => new()
    {
        Id = Guid.NewGuid(),
        Name = "infrastructure-runner",
        Purpose = RunnerPurposes.Infrastructure
    };

    private static RunnerHost CreateHost(Guid infrastructureRunnerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "legacy-host",
        Provider = RunnerHostProviders.Local,
        InfrastructureRunnerId = infrastructureRunnerId
    };

    private static HostedRunner CreateHostedRunner(Guid ownerId, Guid hostId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        RunnerHostId = hostId,
        Name = "legacy-runner",
        ContainerName = "legacy-runner",
        Status = HostedRunnerStatuses.Provisioning
    };

    private static HostedRunnerDeployment CreateProvisioningOperation(Guid hostedRunnerId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        HostedRunnerId = hostedRunnerId,
        Action = HostedRunnerDeploymentActions.Provision,
        Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
        RegistrationDeadlineUtc = now.AddMinutes(5)
    };

    private static RunnerEnrollment CreateEnrollment(
        Guid ownerId, Guid hostId, Guid hostedRunnerId, Guid operationId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        RunnerHostId = hostId,
        HostedRunnerId = hostedRunnerId,
        OperationId = operationId,
        RequestedByUserId = ownerId,
        ExpectedPurpose = RunnerPurposes.Managed,
        ExpectedCapabilitiesJson = RunnerCapabilities.Serialize(
            RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed), RunnerPurposes.Managed),
        EnrollmentTokenHash = HashToken(Token),
        InstallationId = $"legacy_{hostedRunnerId:N}",
        Status = RunnerEnrollmentStatuses.Pending,
        CreatedAtUtc = now,
        ExpiresAtUtc = now.AddMinutes(5)
    };

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private sealed record LegacyEnrollmentSeed(HostedRunner HostedRunner, RunnerEnrollment Enrollment);
}

internal sealed record LegacyEnrollmentOutcome(
    RunnerEnrollmentExchangeResponse? Response,
    string Status,
    string? FailureReason,
    int RunnerCount,
    Guid? LinkedRunnerId);
