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
        => CreateWithSecretGrantService(db, auditEventService, hostedRunnerOptions).Service;

    public static RunnerEnrollmentTestServices CreateWithSecretGrantService(
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
        var hostedOptions = Options.Create(hostedRunnerOptions ?? new HostedRunnerOptions { DockerImage = "test/automaton-runner:latest" });
        var grants = new ExecutionSecretGrantService(db, protector);
        var service = new RunnerEnrollmentService(
            db,
            new InfrastructureRunnerEnrollmentExchangeService(
                db,
                apiKeyService,
                auditEventService,
                NullLogger<InfrastructureRunnerEnrollmentExchangeService>.Instance),
            new RunnerEnrollmentAdministrationService(
                db,
                auditEventService,
                Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
                hostedOptions));
        return new RunnerEnrollmentTestServices(service, grants);
    }
}

internal sealed record RunnerEnrollmentTestServices(
    RunnerEnrollmentService Service,
    ExecutionSecretGrantService SecretGrants);
