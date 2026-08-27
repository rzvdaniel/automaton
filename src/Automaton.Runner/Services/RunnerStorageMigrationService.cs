using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Performs the one-time local migration before runtime recovery starts.
/// </summary>
public sealed class RunnerStorageMigrationService : IHostedService
{
    private readonly RunnerWorkspaceService workspace;
    private readonly ILogger<RunnerStorageMigrationService> logger;

    public RunnerStorageMigrationService(
        RunnerWorkspaceService workspace,
        ILogger<RunnerStorageMigrationService> logger)
    {
        this.workspace = workspace;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            workspace.MigrateLegacyLayout();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Runner storage migration failed; refusing to continue with an ambiguous layout.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
