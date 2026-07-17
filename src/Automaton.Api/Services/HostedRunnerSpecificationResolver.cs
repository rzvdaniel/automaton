using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public interface IHostedRunnerSpecificationResolver
{
    Task<HostedRunnerSpecification> ResolveAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class HostedRunnerSpecificationResolver(ApplicationDbContext db) : IHostedRunnerSpecificationResolver
{
    public async Task<HostedRunnerSpecification> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await db.HostedRunnerPlatformSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            return CreateFallbackSpecification();
        }

        return await db.HostedRunnerSpecifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == settings.DefaultSpecificationId && x.IsActive, cancellationToken)
            ?? CreateFallbackSpecification();
    }

    private static HostedRunnerSpecification CreateFallbackSpecification()
        => new()
        {
            Id = HostedRunnerSpecificationIds.Free,
            Code = "free",
            Name = "Free",
            Revision = 1,
            Resources = new HostedRunnerResources()
        };
}
