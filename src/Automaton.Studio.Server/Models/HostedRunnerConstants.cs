namespace Automaton.Studio.Server.Models;

public static class RunnerHostProviders
{
    public const string Hetzner = "hetzner";
    public const string Local = "local";
}

public static class RunnerHostStatuses
{
    public const string Active = "active";
    public const string Draining = "draining";
    public const string Disabled = "disabled";
}

public static class HostedRunnerStatuses
{
    public const string Pending = "pending";
    public const string Provisioning = "provisioning";
    public const string Running = "running";
    public const string Failed = "failed";
    public const string Disabled = "disabled";
    public const string Deleting = "deleting";
}

public static class HostedRunnerDeploymentActions
{
    public const string Provision = "provision";
    public const string Deprovision = "deprovision";
    public const string Restart = "restart";
    public const string Reprovision = "reprovision";
}

public static class HostedRunnerDeploymentStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public static class HostedRunnerDeploymentModes
{
    public const string DryRun = "dryRun";
    public const string SshDocker = "sshDocker";
}
