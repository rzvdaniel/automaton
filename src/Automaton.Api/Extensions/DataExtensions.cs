using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Extensions
{
    public static class DataExtensions
    {
        public static async Task<WebApplication> ApplyMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);

            var specification = await dbContext.HostedRunnerSpecifications
                .SingleOrDefaultAsync(x => x.Id == HostedRunnerSpecificationIds.Free, cancellationToken);
            if (specification == null)
            {
                specification = new HostedRunnerSpecification
                {
                    Id = HostedRunnerSpecificationIds.Free,
                    Code = "free",
                    Name = "Free Hosted Runner",
                    Description = "Default managed runtime for Automaton accounts.",
                    Resources = new HostedRunnerResources()
                };
                dbContext.HostedRunnerSpecifications.Add(specification);
                dbContext.HostedRunnerSpecificationRevisions.Add(new HostedRunnerSpecificationRevision
                {
                    Id = Guid.NewGuid(),
                    SpecificationId = specification.Id,
                    Revision = specification.Revision,
                    SchemaVersion = specification.SchemaVersion,
                    Name = specification.Name,
                    Description = specification.Description,
                    Resources = HostedRunnerSpecificationSnapshotFactory.CloneResources(specification.Resources),
                    CreatedAtUtc = specification.CreatedAtUtc
                });
            }

            if (!await dbContext.HostedRunnerPlatformSettings.AnyAsync(cancellationToken))
            {
                dbContext.HostedRunnerPlatformSettings.Add(new HostedRunnerPlatformSettings
                {
                    Id = HostedRunnerPlatformSettingsIds.Default,
                    DefaultSpecificationId = specification.Id,
                    ProvisioningEnabled = true,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var guidanceService = scope.ServiceProvider.GetRequiredService<IAiGuidanceRuntimeService>();
            await guidanceService.SeedMissingDefaultsAsync(cancellationToken);

            return app;
        }
    }
}
