using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerBootstrapTestAssertions
{
    public static async Task AssertProvisioningQueuedAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        BootstrapRecordingEventPublisher publisher,
        BootstrapRecordingApplicationEventService applicationEvents,
        Guid appId,
        Guid ownerId)
    {
        await AssertProvisioningResultAsync(service, appId, ownerId);
        AssertProvisioningEventPublished(publisher);
        await AssertProvisioningRecordPersistedAsync(db, applicationEvents);
    }

    private static async Task AssertProvisioningResultAsync(
        ManagedRunnerBootstrapService service,
        Guid appId,
        Guid ownerId)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);
        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.PreparingManagedRuntime));
        Assert.That(result.RequiresRetry, Is.True);
    }

    private static void AssertProvisioningEventPublished(BootstrapRecordingEventPublisher publisher)
    {
        var requests = publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>().ToArray();
        Assert.That(requests, Has.Length.EqualTo(1));
        Assert.That(requests[0].OperationId, Is.Not.EqualTo(Guid.Empty));
    }

    private static async Task AssertProvisioningRecordPersistedAsync(
        ApplicationDbContext db,
        BootstrapRecordingApplicationEventService applicationEvents)
    {
        var hostedRunner = await db.HostedRunners.SingleAsync();
        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(hostedRunner.RunnerHostId, Is.Not.Null);
        Assert.That(applicationEvents.Events.Select(x => x.EventType), Does.Contain(ApplicationEventTypes.ManagedRuntimePreparing));
    }

    public static async Task AssertLegacyRunnerIsNotSelectedAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        BootstrapRecordingEventPublisher publisher,
        Guid appId,
        Guid ownerId)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.Not.EqualTo(AppDeployStates.PreparingManagedRuntime));
        Assert.That(result.State, Is.Not.EqualTo(AppDeployStates.ManagedRunnerWaitingForConnection));
        Assert.That(result.RequiresRetry, Is.False);
        Assert.That(result.Message, Does.Contain("No compatible application Runner"));
        Assert.That(await DefaultTargetRunnerIdAsync(db, appId), Is.Null);
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>(), Is.Empty);
    }

    public static async Task AssertUnavailableAsync(
        ManagedRunnerBootstrapService service,
        Guid appId,
        Guid ownerId,
        string state,
        string message,
        bool containsMessage)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(state));
        if (containsMessage)
            Assert.That(result.Message, Does.Contain(message));
        else
            Assert.That(result.Message, Is.EqualTo(message));
    }

    public static async Task AssertUnavailableAndAssignmentAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        Guid appId,
        Guid ownerId,
        string state,
        string message,
        Guid? expectedRunnerId,
        bool containsMessage)
    {
        await AssertUnavailableAsync(service, appId, ownerId, state, message, containsMessage);
        Assert.That(await DefaultTargetRunnerIdAsync(db, appId), Is.EqualTo(expectedRunnerId));
    }

    public static async Task AssertReadyForSelfHostedRunnerAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        Guid appId,
        Guid ownerId,
        Guid runnerId)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);

        Assert.That(result.IsReady, Is.True);
        Assert.That(result.RunnerId, Is.EqualTo(runnerId));
        Assert.That(await db.HostedRunners.AnyAsync(), Is.False);
    }

    public static async Task AssertProvisionedInsteadOfLegacyInfrastructureAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        BootstrapRecordingEventPublisher publisher,
        Guid appId,
        Guid ownerId)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>().Count(), Is.EqualTo(1));
        Assert.That(await DefaultTargetRunnerIdAsync(db, appId), Is.Null);
    }

    public static async Task AssertLegacyInfrastructureAssignmentUnavailableAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        Guid appId,
        Guid ownerId,
        Guid runnerId)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Does.Contain("Infrastructure Runners"));
        Assert.That(await DefaultTargetRunnerIdAsync(db, appId), Is.EqualTo(runnerId));
    }

    public static async Task AssertMissingAssignmentUnavailableAsync(
        ApplicationDbContext db,
        ManagedRunnerBootstrapService service,
        Guid appId,
        Guid ownerId)
    {
        var result = await service.EnsureRunnerAssignmentAsync(appId, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Does.Contain("not found"));
        Assert.That(await DefaultTargetRunnerIdAsync(db, appId), Is.Not.Null);
    }

    private static Task<Guid?> DefaultTargetRunnerIdAsync(ApplicationDbContext db, Guid appId) =>
        db.AppDeploymentTargets.Where(target => target.WebAppId == appId && target.IsDefault)
            .Select(target => target.ExecutionRunnerId).SingleAsync();
}
