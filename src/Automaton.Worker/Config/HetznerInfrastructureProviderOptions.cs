namespace Automaton.Worker.Config;

public sealed class HetznerInfrastructureProviderOptions
{
    public const string SectionName = "InfrastructureProviders:Hetzner";
    public bool Enabled { get; set; }
    public string ApiToken { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.hetzner.cloud/v1";
    public string DefaultLocation { get; set; } = string.Empty;
    public string DefaultServerType { get; set; } = string.Empty;
    public string DockerImage { get; set; } = "docker-ce";
    public List<string> SshKeyIds { get; set; } = [];
    public string? PrivateNetworkId { get; set; }
    public string InfrastructureRunnerImage { get; set; } = "automaton-runner:latest";
}
