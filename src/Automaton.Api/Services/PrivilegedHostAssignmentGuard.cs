using Automaton.Api.Entities;
using Automaton.Core.Enums;

namespace Automaton.Api.Services;

/// <summary>Requires an explicit acknowledgement before an App is assigned to a privileged host.</summary>
public static class PrivilegedHostAssignmentGuard
{
    public static void EnsureAcknowledged(Runner runner, bool acknowledged)
    {
        if (RunnerProfileRules.IsPrivilegedHost(runner.Purpose, runner.ApplicationProfile) && !acknowledged)
        {
            throw new InvalidOperationException(
                "Assigning an App to a Privileged Host Runner requires explicit host-access acknowledgement.");
        }
    }
}
