using Automaton.Api.Entities;
using Automaton.Core.Enums;

namespace Automaton.Api.Services;

/// <summary>Requires an explicit acknowledgement before an App is assigned to a privileged host.</summary>
public static class PrivilegedHostAssignmentGuard
{
    public static void EnsureApproved(AppDeploymentTarget target, Runner runner)
    {
        if (RunnerProfileRules.IsPrivilegedHost(runner.Purpose, runner.ApplicationProfile) &&
            !target.PrivilegedHostAccessApproved)
        {
            throw new InvalidOperationException(
                "This deployment target must have persisted Privileged Host access approval before it can be assigned to a Privileged Host Runner.");
        }
    }
}
