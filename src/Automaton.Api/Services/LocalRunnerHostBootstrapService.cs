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
        if (!config.Enabled || !config.AutoCreateLocalRunnerHost)
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
            if (UpdateExistingHost(existingHost, config))
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
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            TotalCpuCores = config.LocalRunnerHostCpuCores,
            TotalMemoryMb = config.LocalRunnerHostMemoryMb,
            TotalDiskMb = config.LocalRunnerHostDiskMb,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.RunnerHosts.Add(host);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded local runner host {RunnerHostId} for runtime-local managed runners", host.Id);
    }

    private static bool UpdateExistingHost(RunnerHost host, HostedRunnerOptions config)
    {
        var changed = false;

        if (host.Status != RunnerHostStatuses.Active)
        {
            host.Status = RunnerHostStatuses.Active;
            changed = true;
        }

        if (host.ProvisioningStatus != RunnerHostProvisioningStatuses.Ready)
        {
            host.ProvisioningStatus = RunnerHostProvisioningStatuses.Ready;
            host.ValidatedAtUtc ??= DateTime.UtcNow;
            changed = true;
        }

        if (!host.HostedRunnerSchedulingEnabled)
        {
            host.HostedRunnerSchedulingEnabled = true;
            changed = true;
        }

        if (host.TotalCpuCores != config.LocalRunnerHostCpuCores)
        {
            host.TotalCpuCores = config.LocalRunnerHostCpuCores;
            changed = true;
        }

        if (host.TotalMemoryMb != config.LocalRunnerHostMemoryMb)
        {
            host.TotalMemoryMb = config.LocalRunnerHostMemoryMb;
            changed = true;
        }

        if (host.TotalDiskMb != config.LocalRunnerHostDiskMb)
        {
            host.TotalDiskMb = config.LocalRunnerHostDiskMb;
            changed = true;
        }

        return changed;
    }
}
