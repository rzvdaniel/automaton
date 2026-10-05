using Automaton.Api.Entities;
using Automaton.Api.Models;

namespace Automaton.Api.Services;

public static class HostedRunnerComputeReservationPolicy
{
    public static bool OccupiesCapacity(HostedRunner runner)
        => runner.DeletedAt == null
            && (runner.Status is HostedRunnerStatuses.Pending or HostedRunnerStatuses.Provisioning
                or HostedRunnerStatuses.Reprovisioning or HostedRunnerStatuses.Running
                || runner.Status == HostedRunnerStatuses.Failed && runner.ContainerId != null);

    public static (decimal CpuCores, int MemoryMb) Calculate(HostedRunner runner)
    {
        var current = runner.SpecificationSnapshot.Resources;
        var pending = runner.PendingSpecificationSnapshot?.Resources ?? current;
        return (Math.Max(current.CpuLimit, pending.CpuLimit),
            Math.Max(current.MemoryLimitMb, pending.MemoryLimitMb));
    }
}
