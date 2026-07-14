using Automaton.Api.Models;

namespace Automaton.Api.Config;

public class HostedRunnerOptions
{
    public bool Enabled { get; set; }
    public string DeploymentMode { get; set; } = HostedRunnerDeploymentModes.DryRun;
    public string ServerUrl { get; set; } = "https://api.automaton.monster";
    public string DockerImage { get; set; } = "automaton-runner:latest";
    public string DockerNetwork { get; set; } = "automaton_runner_network";
    public string? RuntimeDockerNetwork { get; set; }
    // The runner gateway owns this port; the internal FastAPI process listens on 8001.
    public int RuntimePort { get; set; } = 8080;
    // Remote hosts publish each hosted Runner gateway on a unique host port.
    public int RemoteRuntimePortStart { get; set; } = 20000;
    public int RemoteRuntimePortEnd { get; set; } = 20999;
    public string DataRoot { get; set; } = "/opt/automaton/hosted-runners";
    public bool AutoCreateLocalRunnerHost { get; set; }
    public string LocalRunnerHostName { get; set; } = "runtime-local";
    public decimal LocalRunnerHostCpuCores { get; set; } = 8m;
    public int LocalRunnerHostMemoryMb { get; set; } = 16384;
    public int LocalRunnerHostDiskMb { get; set; } = 65536;
    public string SshUsername { get; set; } = "automaton-runner-provisioner";
    public int SshPort { get; set; } = 22;
    public string SshPrivateKeyPath { get; set; } = "/run/secrets/hosted_runner_ssh_key";
    public string? SshPrivateKeyPassphrase { get; set; }
    public int SshConnectTimeoutSeconds { get; set; } = 20;
    public int SshCommandTimeoutSeconds { get; set; } = 180;
}
