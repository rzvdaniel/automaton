namespace Automaton.Messaging;

public static class EventTopics
{
    public const string RunnerExecutionRequested = "runner-execution.requested";
    public const string UserRegistered = "user.registered";
    public const string InfrastructureCommandRequested = "infrastructure-command.requested";
    public const string ScheduledActionExecutionRequested = "scheduled-action.execution-requested";
    public const string DeploymentPackageRequested = "deployment-package.execution-requested";
}
