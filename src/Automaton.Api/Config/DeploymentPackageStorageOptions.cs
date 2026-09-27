namespace Automaton.Api.Config;

public sealed class DeploymentPackageStorageOptions
{
    public const string SectionName = "DeploymentPackageStorage";

    public long MaximumStoredBytes { get; set; } = 1024L * 1024 * 1024;

    public int MaximumStoredPackages { get; set; } = 100;
}
