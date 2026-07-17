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
            var specification = await db.HostedRunnerSpecifications
                .SingleOrDefaultAsync(x => x.Id == HostedRunnerSpecificationIds.Free, cancellationToken);

            if (specification == null)
            {
                var now = DateTime.UtcNow;
                specification = new HostedRunnerSpecification
                {
                    Id = HostedRunnerSpecificationIds.Free,
                    Code = "free",
                    Name = "Free",
                    Revision = 1,
                    Resources = new HostedRunnerResources(),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    IsActive = true
                };
                db.HostedRunnerSpecifications.Add(specification);
                db.HostedRunnerSpecificationRevisions.Add(new HostedRunnerSpecificationRevision
                {
                    Id = Guid.NewGuid(),
                    SpecificationId = specification.Id,
                    Revision = specification.Revision,
                    SchemaVersion = specification.SchemaVersion,
                    Resources = HostedRunnerSpecificationSnapshotFactory.CloneResources(specification.Resources),
                    Name = specification.Name,
                    Description = specification.Description,
                    CreatedAtUtc = now
                });
            }

            if (!specification.IsActive)
            {
                specification.IsActive = true;
                specification.UpdatedAtUtc = DateTime.UtcNow;
            }

            db.HostedRunnerPlatformSettings.Add(new HostedRunnerPlatformSettings
            {
                Id = HostedRunnerPlatformSettingsIds.Default,
                DefaultSpecificationId = specification.Id,
                ProvisioningEnabled = true,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            return specification;
        }

        return await db.HostedRunnerSpecifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == settings.DefaultSpecificationId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Hosted runner platform settings reference missing or inactive specification '{settings.DefaultSpecificationId}'.");
    }
}
