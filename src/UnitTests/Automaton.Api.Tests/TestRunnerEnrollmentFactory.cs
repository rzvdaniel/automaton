using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class TestRunnerEnrollmentFactory
{
    public static RunnerEnrollmentService Create(
        ApplicationDbContext db,
        IAuditEventService auditEventService,
        HostedRunnerOptions? hostedRunnerOptions = null)
    {
        var apiKeyService = new ApiKeyService(
            db,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditEventService);
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        return new RunnerEnrollmentService(
            db,
            apiKeyService,
            auditEventService,
            Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
            Options.Create(hostedRunnerOptions ?? new HostedRunnerOptions { DockerImage = "test/automaton-runner:latest" }),
            new SecretStore(db, protector),
            new ExecutionSecretGrantService(db, protector),
            NullLogger<RunnerEnrollmentService>.Instance);
    }
}
