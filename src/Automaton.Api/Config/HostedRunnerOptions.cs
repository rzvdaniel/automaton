namespace Automaton.Api.Config;

public class HostedRunnerOptions
{
    public string ServerUrl { get; set; } = "https://api.automaton.monster";
    public string DockerImage { get; set; } = "automaton-runner:latest";
    public string DockerNetwork { get; set; } = "automaton_runner_network";
    public int RestartReconnectTimeoutSeconds { get; set; } = 120;
    public int LifecycleRecoveryDelaySeconds { get; set; } = 300;
    public int LifecycleRecoveryRetrySeconds { get; set; } = 300;
}
