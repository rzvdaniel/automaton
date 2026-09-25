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

        var isHostedRunner = await dbContext.HostedRunners
            .AnyAsync(x => x.RunnerId == runnerId && x.DeletedAt == null, cancellationToken);
        if (isHostedRunner)
            throw new InvalidOperationException("Hosted runners cannot be deleted from the generic runners page. Restart the hosted runner instead.");
    }
}
