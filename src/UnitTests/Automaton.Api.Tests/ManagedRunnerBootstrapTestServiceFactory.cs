using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerBootstrapTestServiceFactory
{
    public static ManagedRunnerBootstrapService CreateService(
        ApplicationDbContext db,
        BootstrapRecordingEventPublisher publisher,
        BootstrapRecordingApplicationEventService applicationEvents)
    {
        var hostedRunnerOptions = CreateHostedRunnerOptions();
        var auditEventService = new BootstrapNoOpAuditEventService();
        var provisioningService = CreateProvisioningService(db, publisher, auditEventService, hostedRunnerOptions);
        var runnerAssignmentPolicy = CreateRunnerAssignmentPolicy(db);

        return new ManagedRunnerBootstrapService(db, provisioningService, publisher, applicationEvents, runnerAssignmentPolicy);
    }

    public static BootstrapTestOptionsSnapshot<HostedRunnerOptions> CreateHostedRunnerOptions()
        => new(new HostedRunnerOptions { Enabled = true, RuntimePort = 8080 });

    public static HostedRunnerProvisioningService CreateProvisioningService(
        ApplicationDbContext db,
        BootstrapRecordingEventPublisher publisher,
        IAuditEventService auditEventService,
        IOptionsSnapshot<HostedRunnerOptions> hostedRunnerOptions)
    {
        var enrollmentService = TestRunnerEnrollmentFactory.Create(db, auditEventService);
        var hostPlacementService = new HostedRunnerHostPlacementService(db, hostedRunnerOptions);
        var preparationService = new HostedRunnerProvisioningPreparationService(
            db, enrollmentService, auditEventService, hostedRunnerOptions, hostPlacementService,
            NullLogger<HostedRunnerProvisioningPreparationService>.Instance);
        var provisioningPolicy = new HostedRunnerProvisioningPolicyService(
            db,
            new SubscriptionResourcePolicyResolver(new HostedRunnerSpecificationResolver(db)),
            NullLogger<HostedRunnerProvisioningPolicyService>.Instance);
        return new HostedRunnerProvisioningService(
            db, publisher, hostedRunnerOptions, provisioningPolicy,
            new AccountStorageProjectAllocationService(db),
            NullLogger<HostedRunnerProvisioningService>.Instance, preparationService);
    }

    public static AppRunnerAssignmentPolicy CreateRunnerAssignmentPolicy(ApplicationDbContext db)
    {
        var userContextService = new UserContextService(new HttpContextAccessor());
        var authorizationService = new ResourceAuthorizationService(db, userContextService);
        var runnerAuthorizationPolicy = new AppRunnerAuthorizationPolicy(db, authorizationService);
        var deploymentTargetResolver = new AppDeploymentTargetResolver(db, authorizationService, runnerAuthorizationPolicy);
        return new AppRunnerAssignmentPolicy(
            db,
            new HostedRunnerAppCapacityService(db),
            runnerAuthorizationPolicy,
            deploymentTargetResolver);
    }

}
