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

            var specification = await EnsureFreeHostedRunnerSpecificationAsync(dbContext, cancellationToken);
            await EnsureHostedRunnerPlatformSettingsAsync(dbContext, specification, cancellationToken);
            await EnsureFreeSubscriptionPlanAsync(dbContext, specification, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);

            var guidanceService = scope.ServiceProvider.GetRequiredService<IAiGuidanceRuntimeService>();
            await guidanceService.SeedMissingDefaultsAsync(cancellationToken);

            return app;
        }

        private static async Task<HostedRunnerSpecification> EnsureFreeHostedRunnerSpecificationAsync(
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
        {
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

            return specification;
        }

        private static async Task EnsureHostedRunnerPlatformSettingsAsync(
            ApplicationDbContext dbContext,
            HostedRunnerSpecification specification,
            CancellationToken cancellationToken)
        {
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
        }

        private static async Task EnsureFreeSubscriptionPlanAsync(
            ApplicationDbContext dbContext,
            HostedRunnerSpecification specification,
            CancellationToken cancellationToken)
        {
            var freePlan = await dbContext.SubscriptionPlans
                .SingleOrDefaultAsync(x => x.Id == SubscriptionPlanIds.Free, cancellationToken);
            if (freePlan == null)
            {
                freePlan = new SubscriptionPlan
                {
                    Id = SubscriptionPlanIds.Free,
                    Name = "Free",
                    Description = "Free Automaton plan.",
                    IsDefault = true,
                    IsActive = true,
                    SortOrder = 0,
                    CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
                    {
                        HostedRunner = new HostedRunnerCapability
                        {
                            Enabled = true,
                            Visibility = HostedRunnerVisibility.Public,
                            SpecificationId = specification.Id
                        }
                    })
                };
                dbContext.SubscriptionPlans.Add(freePlan);
            }
            else
            {
                var capabilities = PlanCapabilitiesSerializer.Deserialize(freePlan.CapabilitiesJson);
                if (capabilities.HostedRunner.Enabled && !capabilities.HostedRunner.SpecificationId.HasValue)
                {
                    capabilities.HostedRunner.SpecificationId = specification.Id;
                    freePlan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(capabilities);
                }
            }
        }
    }
}
