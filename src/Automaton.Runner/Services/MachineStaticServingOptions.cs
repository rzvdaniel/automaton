namespace Automaton.Runner.Console.Services;

public sealed class MachineStaticServingOptions
{
    public string Image { get; set; } = "nginx:alpine";
    public int ContainerPort { get; set; } = 8080;
    public decimal CpuLimitCores { get; set; } = 0.5m;
    public int MemoryLimitMb { get; set; } = 256;
    public int PidsLimit { get; set; } = 128;
    public int DockerLogMaxSizeMb { get; set; } = 10;
    public int DockerLogMaxFiles { get; set; } = 3;
    public long MaximumPublishedFileBytes { get; set; } = 250L * 1024 * 1024;
    public int MaximumPublishedFiles { get; set; } = 20_000;

    public bool IsValid()
        => !string.IsNullOrWhiteSpace(Image)
            && !Image.Any(char.IsWhiteSpace)
            && !Image.Contains('\0')
            && ContainerPort is >= 1 and <= 65_535
            && new Automaton.Core.Models.HostContainerResourceLimits(CpuLimitCores, MemoryLimitMb, PidsLimit).IsValid()
            && DockerLogMaxSizeMb is >= 1 and <= 1024
            && DockerLogMaxFiles is >= 1 and <= 20
            && MaximumPublishedFileBytes > 0
            && MaximumPublishedFiles > 0;
}

public sealed record MachineStaticServerEndpoint(string Address, int Port, string PathPrefix = "")
{
    public Uri BaseAddress => new UriBuilder(Uri.UriSchemeHttp, Address, Port).Uri;
}
