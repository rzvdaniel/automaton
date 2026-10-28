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
    public async Task EnsureCanDeleteAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanDeleteRunnerAsync(runnerId, cancellationToken))
        {
            var isPartOfProject = await dbContext.ProjectRunners
                .AnyAsync(pr => pr.RunnerId == runnerId, cancellationToken);

            if (isPartOfProject)
                throw new InvalidOperationException($"Cannot delete runner {runnerId} because it is part of one or more projects. Remove it from all projects first.");

            throw new UnauthorizedAccessException($"You do not have permission to delete runner {runnerId}");
        }

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
