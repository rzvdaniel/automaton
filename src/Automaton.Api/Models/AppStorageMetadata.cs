namespace Automaton.Api.Models;

public class AppStorageMetadata
{
    public int Version { get; set; } = 1;
    public AppSettings? Settings { get; set; }
    public GitHubSettings? GitHub { get; set; }
    public IList<string>? ConnectedFlowIds { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public string DevelopmentMode { get; set; } = Constants.AppDevelopmentModes.Studio;
    public AppComponentStorageMetadata Frontend { get; set; } = new();
    public AppComponentStorageMetadata Backend { get; set; } = new();
}

public class AppComponentStorageMetadata
{
    public bool SourceAvailable { get; set; } = true;
    public DateTime? SourceModifiedAtUtc { get; set; }
    public string RuntimeSource { get; set; } = Constants.AppRuntimeSources.Studio;
    public bool RuntimeAvailable { get; set; }
    public DateTime? RuntimeDeployedAtUtc { get; set; }
}
