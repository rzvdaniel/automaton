using Automaton.Api.Data;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Enforces the ownership and lifecycle restrictions for deleting generic Runners.
/// </summary>
public sealed class RunnerDeletionPolicy(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService)
{
    public async Task EnsureCanDeleteAsync(Guid runnerId, CancellationToken cancellationToken, bool abandonOffline = false)
    {
        if (!await authorizationService.CanDeleteRunnerAsync(runnerId, cancellationToken))
        {
            // Only the Runner's own user is told a project blocks the delete; everyone else gets the usual refusal.
            var isPartOfProject = await authorizationService.HasPersonalRunnerAccessAsync(runnerId, cancellationToken)
                && await dbContext.ProjectRunners
                    .AnyAsync(pr => pr.RunnerId == runnerId && pr.Project != null && pr.Project.IsActive, cancellationToken);

            if (isPartOfProject)
                throw new InvalidOperationException($"Cannot delete runner {runnerId} because it is part of one or more projects. Remove it from all projects first.");

            throw new UnauthorizedAccessException($"You do not have permission to delete runner {runnerId}");
        }

        // An offline Runner's apps are deleted first, and its pending operations are revoked with it.
        if (abandonOffline) return;

        var assignments = await dbContext.AppDeploymentTargets
            .Where(target => target.ExecutionRunnerId == runnerId)
            .Select(target => target.WebApp.Name + " / " + target.Name)
            .ToListAsync(cancellationToken);
        if (assignments.Count > 0)
            throw new InvalidOperationException(
                $"Move or delete all assigned app environments before deleting this Runner: {string.Join(", ", assignments)}.");
        if (await dbContext.RunnerExecutionOperations.AnyAsync(
                operation => operation.RunnerId == runnerId && operation.CompletedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("Wait for the Runner's pending operations to finish before preparing removal.");
    }
}
