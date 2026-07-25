using System.Text.Json;

namespace Automaton.Api.Models;

public sealed class SaveActionScheduleRequest
{
    public Guid ApplicationId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ExecutionScope { get; set; } = "AppRunner";
    public string ScheduleType { get; set; } = "Interval";
    public DateTime? RunAtUtc { get; set; }
    public int? IntervalSeconds { get; set; }
    public string? CronExpression { get; set; }
    public string TimeZone { get; set; } = "Etc/UTC";
    public bool Enabled { get; set; } = true;
    public string ConcurrencyPolicy { get; set; } = "SkipIfRunning";
    public string MisfirePolicy { get; set; } = "Skip";
    public JsonElement Input { get; set; }
}
