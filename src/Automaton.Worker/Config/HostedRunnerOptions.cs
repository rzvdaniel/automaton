namespace Automaton.Worker.Config;

public class HostedRunnerOptions
{
    public bool Enabled { get; set; }
    public string DeploymentMode { get; set; } = "dryRun";
    public string ServerUrl { get; set; } = "https://api.automaton.monster";
    public string DockerImage { get; set; } = "automaton-runner:latest";
    public string DockerNetwork { get; set; } = "automaton_runner_network";
    public string? RuntimeDockerNetwork { get; set; }
    public int RuntimePort { get; set; } = 8001;
    public string DataRoot { get; set; } = "/opt/automaton/hosted-runners";
    public string SshUsername { get; set; } = "automaton-runner-provisioner";
    public int SshPort { get; set; } = 22;
    public string SshPrivateKeyPath { get; set; } = "/run/secrets/hosted_runner_ssh_key";
    public string? SshPrivateKeyPassphrase { get; set; }
    public int SshConnectTimeoutSeconds { get; set; } = 20;
    public int SshCommandTimeoutSeconds { get; set; } = 180;
}
