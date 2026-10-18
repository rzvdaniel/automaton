using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class MachineActionExecutionPolicy
{
    public static Guid GetRunnerId()
    {
        var configured = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID");
        if (!Guid.TryParse(configured, out var runnerId) || runnerId == Guid.Empty)
            throw new InvalidOperationException("Machine Runner identity is required for isolated Action execution.");
        return runnerId;
    }

    public static HostContainerScope CreateScope(Guid appId, Guid targetId)
        => new(Guid.Empty, Guid.Empty, appId, targetId, Guid.Empty, GetRunnerId());
}
