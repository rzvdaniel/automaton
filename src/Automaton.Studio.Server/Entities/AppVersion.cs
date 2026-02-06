using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Entities;

public class AppVersion
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public int VersionNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public AppVersionTrigger Trigger { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? AiSessionId { get; set; }
    public string? AiModel { get; set; }
    public string SnapshotPath { get; set; } = string.Empty;
    public string? FilesChangeSummary { get; set; }

    public virtual WebApp WebApp { get; set; }
    public virtual ICollection<AppVersionStep> Steps { get; set; } = new List<AppVersionStep>();
}
