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
        var hostedOptions = Options.Create(hostedRunnerOptions ?? new HostedRunnerOptions { DockerImage = "test/automaton-runner:latest" });
        return new RunnerEnrollmentService(
            db,
            new HostedRunnerEnrollmentExchangeService(
                db,
                apiKeyService,
                auditEventService,
                NullLogger<HostedRunnerEnrollmentExchangeService>.Instance),
            new InfrastructureRunnerEnrollmentExchangeService(
                db,
                apiKeyService,
                auditEventService,
                NullLogger<InfrastructureRunnerEnrollmentExchangeService>.Instance),
            new RunnerEnrollmentAdministrationService(
                db,
                auditEventService,
                Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
                hostedOptions),
            new HostedRunnerEnrollmentProvisioningService(
                db,
                apiKeyService,
                auditEventService,
                Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
                new SecretStore(db, protector),
                new ExecutionSecretGrantService(db, protector)));
    }
}
