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

            await EnsureFreeSubscriptionPlanAsync(dbContext, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);

            var guidanceService = scope.ServiceProvider.GetRequiredService<IAiGuidanceRuntimeService>();
            await guidanceService.SeedMissingDefaultsAsync(cancellationToken);

            return app;
        }

        private static async Task EnsureFreeSubscriptionPlanAsync(
            ApplicationDbContext dbContext,
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
                    CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(PlanCapabilities.Default)
                };
                dbContext.SubscriptionPlans.Add(freePlan);
            }
        }
    }
}
