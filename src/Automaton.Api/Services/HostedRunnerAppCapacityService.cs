using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Automaton.Api.Services;

public sealed class HostedRunnerAppCapacityService(ApplicationDbContext dbContext)
{
    public async Task AssignAsync(
        WebApp app,
        Guid runnerId,
        CancellationToken cancellationToken = default)
    {
        var hostedRunner = await dbContext.HostedRunners
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunnerId == runnerId && x.DeletedAt == null, cancellationToken);

        if (hostedRunner == null)
        {
            app.ExecutionRunnerId = runnerId;
            return;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        if (transaction != null && dbContext.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM HostedRunners WHERE RunnerId = {runnerId} AND DeletedAt IS NULL FOR UPDATE",
                cancellationToken);
        }

        hostedRunner = await dbContext.HostedRunners
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunnerId == runnerId && x.DeletedAt == null, cancellationToken);
        if (hostedRunner == null)
        {
            app.ExecutionRunnerId = runnerId;
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return;
        }

        var assignedCount = await dbContext.WebApps
            .CountAsync(x => x.ExecutionRunnerId == runnerId && x.Id != app.Id, cancellationToken);

        var maximumApps = hostedRunner.SpecificationSnapshot.Resources.MaximumApps;
        if (assignedCount >= maximumApps)
        {
            throw new InvalidOperationException(
                $"Hosted runner '{hostedRunner.Name}' has reached its maximum of {maximumApps} apps.");
        }

        app.ExecutionRunnerId = runnerId;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
    }
}
