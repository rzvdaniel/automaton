namespace Automaton.Api.Models;

public static class RunnerHostProviders
{
    public const string Hetzner = "hetzner";
    public const string Local = "local";
}

public static class RunnerHostProvisioningTransports
{
    public const string LocalDocker = "localDocker";
    public const string InfrastructureRunner = "infrastructureRunner";
    public const string Ssh = "ssh";
}

public static class RunnerHostRuntimeConnectivityModes
{
    public const string LocalDockerNetwork = "localDockerNetwork";
    public const string PublicHostPort = "publicHostPort";
}

public static class RunnerHostStatuses
{
    public const string Active = "active";
    public const string Draining = "draining";
    public const string Disabled = "disabled";
}

public static class RunnerHostProvisioningStatuses
{
    public const string Manual = "manual";
    public const string Pending = "pending";
    public const string CreatingServer = "creatingServer";
    public const string AwaitingInfrastructureRunner = "awaitingInfrastructureRunner";
    public const string Validating = "validating";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Deleting = "deleting";
    public const string Deleted = "deleted";
}

public static class RunnerHostProvisioningActions
{
    public const string Create = "create";
    public const string Reconcile = "reconcile";
    public const string RetryEnrollment = "retryEnrollment";
    public const string Validate = "validate";
    public const string Delete = "delete";
}

public static class RunnerHostProvisioningOperationStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string AwaitingInfrastructureRunner = "awaitingInfrastructureRunner";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class RunnerEnrollmentStatuses
{
    public const string Pending = "pending";
    public const string Claimed = "claimed";
    public const string Completed = "completed";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
}

public static class HostedRunnerStatuses
{
    public const string Pending = "pending";
    public const string Provisioning = "provisioning";
    public const string Running = "running";
    public const string Failed = "failed";
    public const string Disabled = "disabled";
    public const string Deleting = "deleting";
    public const string DeleteFailed = "deleteFailed";
    public const string Deleted = "deleted";
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
    public const string LocalDocker = "localDocker";
}
