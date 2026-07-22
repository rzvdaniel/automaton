namespace Automaton.Api.Models;

public static class SecretOwnerTypes
{
    public const string User = "user";
    public const string Project = "project";
    public const string App = "app";
    public const string Platform = "platform";
    public const string RunnerHost = "runnerHost";
    public const string HostedRunner = "hostedRunner";
}

public static class SecretDeliveryTypes
{
    public const string EnvironmentVariable = "environmentVariable";
}

public static class SecretDeliveryPolicies
{
    public const string SingleDelivery = "singleDelivery";
    public const string RetryableDelivery = "retryableDelivery";
}
