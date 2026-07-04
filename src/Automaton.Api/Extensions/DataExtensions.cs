using Automaton.Api.Data;
using Automaton.Api.Services;
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

            var guidanceService = scope.ServiceProvider.GetRequiredService<IAiGuidanceRuntimeService>();
            await guidanceService.SeedMissingDefaultsAsync(cancellationToken);

            return app;
        }
    }
}
