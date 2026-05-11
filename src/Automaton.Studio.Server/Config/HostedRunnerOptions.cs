using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Config;

public class HostedRunnerOptions
{
    public bool Enabled { get; set; }
    public string DeploymentMode { get; set; } = HostedRunnerDeploymentModes.DryRun;
    public string ServerUrl { get; set; } = "https://api.automaton.monster";
    public string DockerImage { get; set; } = "automaton-runner:latest";
    public string DockerNetwork { get; set; } = "automaton_runner_network";
    public string DataRoot { get; set; } = "/opt/automaton/hosted-runners";
}
