using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class LocalRunnerHostBootstrapService(
    ApplicationDbContext dbContext,
    IOptionsSnapshot<HostedRunnerOptions> options,
    ILogger<LocalRunnerHostBootstrapService> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        if (!config.Enabled ||
            !config.AutoCreateLocalRunnerHost ||
            !string.Equals(config.DeploymentMode, HostedRunnerDeploymentModes.LocalDocker, StringComparison.Ordinal))
        {
            return;
        }

        var existingHost = await dbContext.RunnerHosts
            .FirstOrDefaultAsync(
                host => host.Provider == RunnerHostProviders.Local
                        && host.ProvisioningTransport == RunnerHostProvisioningTransports.LocalDocker
                        && host.Name == config.LocalRunnerHostName,
                cancellationToken);

        if (existingHost != null)
        {
            var changed = false;

            if (existingHost.Status != RunnerHostStatuses.Active)
            {
                existingHost.Status = RunnerHostStatuses.Active;
                changed = true;
            }

            if (!existingHost.HostedRunnerSchedulingEnabled)
            {
                existingHost.HostedRunnerSchedulingEnabled = true;
                changed = true;
            }

            if (existingHost.TotalCpuCores != config.LocalRunnerHostCpuCores)
            {
                existingHost.TotalCpuCores = config.LocalRunnerHostCpuCores;
                changed = true;
            }

            if (existingHost.TotalMemoryMb != config.LocalRunnerHostMemoryMb)
            {
                existingHost.TotalMemoryMb = config.LocalRunnerHostMemoryMb;
                changed = true;
            }

            if (existingHost.TotalDiskMb != config.LocalRunnerHostDiskMb)
            {
                existingHost.TotalDiskMb = config.LocalRunnerHostDiskMb;
                changed = true;
            }

            if (changed)
            {
                existingHost.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Updated local runner host {RunnerHostId} for runtime-local managed runners", existingHost.Id);
            }

            return;
        }

        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = config.LocalRunnerHostName,
            Provider = RunnerHostProviders.Local,
            ProvisioningTransport = RunnerHostProvisioningTransports.LocalDocker,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            HostedRunnerSchedulingEnabled = true,
            Status = RunnerHostStatuses.Active,
            TotalCpuCores = config.LocalRunnerHostCpuCores,
            TotalMemoryMb = config.LocalRunnerHostMemoryMb,
            TotalDiskMb = config.LocalRunnerHostDiskMb,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.RunnerHosts.Add(host);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded local runner host {RunnerHostId} for runtime-local managed runners", host.Id);
    }
}
