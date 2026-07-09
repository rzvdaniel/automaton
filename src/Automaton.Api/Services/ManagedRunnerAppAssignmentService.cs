using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ManagedRunnerAppAssignmentService
{
    private readonly ApplicationDbContext dbContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly ILogger<ManagedRunnerAppAssignmentService> logger;

    public ManagedRunnerAppAssignmentService(
        ApplicationDbContext dbContext,
        AppFileStorageService fileStorageService,
        ILogger<ManagedRunnerAppAssignmentService> logger)
    {
        this.dbContext = dbContext;
        this.fileStorageService = fileStorageService;
        this.logger = logger;
    }

    public async Task<int> AssignEligibleAppsAsync(
        Guid userId,
        Guid runnerId,
        CancellationToken cancellationToken = default)
    {
        var apps = await dbContext.WebApps
            .Include(x => x.WebAppUsers)
            .Where(x => x.ExecutionRunnerId == null && x.WebAppUsers.Any(u => u.UserId == userId))
            .ToListAsync(cancellationToken);

        var assignedCount = 0;
        foreach (var app in apps)
        {
            var metadata = await fileStorageService.GetAppStorageMetadataAsync(userId, app.Id);
            var hasBackend = metadata?.Backend.SourceAvailable == true || metadata?.Backend.RuntimeAvailable == true;
            if (!hasBackend)
            {
                continue;
            }

            app.ExecutionRunnerId = runnerId;
            assignedCount++;
        }

        if (assignedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Assigned managed runner {RunnerId} to {AssignedCount} eligible app(s) for user {UserId}",
                runnerId,
                assignedCount,
                userId);
        }

        return assignedCount;
    }
}
