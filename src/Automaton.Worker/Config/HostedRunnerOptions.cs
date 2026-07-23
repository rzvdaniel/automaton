namespace Automaton.Worker.Config;

public class HostedRunnerOptions
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "https://api.automaton.monster";
    public string DockerImage { get; set; } = "automaton-runner:latest";
    public string DockerNetwork { get; set; } = "automaton_runner_network";
    public string? RuntimeDockerNetwork { get; set; }
    // The public runtime gateway owns this port. FastAPI remains internal on 8001.
    public int RuntimePort { get; set; } = 8080;
    public string DataRoot { get; set; } = "/opt/automaton/hosted-runners";
}
