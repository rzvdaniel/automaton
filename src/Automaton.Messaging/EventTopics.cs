namespace Automaton.Messaging;

public static class EventTopics
{
    public const string UserRegistered = "user.registered";
    public const string ManagedRunnerProvisionRequested = "managed-runner.provision-requested";
    public const string ManagedRunnerProvisionCompleted = "managed-runner.provision-completed";
    public const string ManagedRunnerProvisionFailed = "managed-runner.provision-failed";
    public const string RunnerHostProvisionRequested = "runner-host.provision-requested";
}
