namespace Automaton.Runner.Console.Models;

public sealed class RunnerInstallationProfile
{
    public string Purpose { get; set; } = string.Empty;
    public string? ApplicationProfile { get; set; }
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public Guid? RunnerHostId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
}
