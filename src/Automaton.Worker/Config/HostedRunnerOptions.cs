namespace Automaton.Worker.Config;

public class HostedRunnerOptions
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "https://api.automaton.monster";
    public string DockerImage { get; set; } = "automaton-runner:latest";
    public string DockerNetwork { get; set; } = "automaton_runner_network";
    public string? RuntimeDockerNetwork { get; set; }
    public int RuntimePort { get; set; } = 8001;
    public string DataRoot { get; set; } = "/opt/automaton/hosted-runners";
}
