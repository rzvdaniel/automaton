namespace Automaton.Studio.Server.Models;

public class AdminAppDetails
{
    public AppModel App { get; set; } = new();
    public Guid OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? OwnerPublicUsername { get; set; }
    public string? ExecutionRunnerName { get; set; }
    public string? ProjectName { get; set; }
    public int SourceFileCount { get; set; }
    public int PublishedFileCount { get; set; }
    public int BackendFileCount { get; set; }
    public long TotalSizeBytes { get; set; }
}

public class AdminMarketplaceModerationRequest
{
    public string? MarketplaceStatus { get; set; }
    public string? MarketplaceModerationReason { get; set; }
}
