using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerBootstrapTestAssertions
{
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
