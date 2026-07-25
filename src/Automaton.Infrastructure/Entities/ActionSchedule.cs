namespace Automaton.Api.Entities;

public static class ActionScheduleTypes
{
    public const string OneTime = "OneTime";
    public const string Interval = "Interval";
    public const string Cron = "Cron";
}

public static class ActionScheduleOwnerTypes
{
    public const string User = "User";
    public const string System = "System";
}

public static class ActionExecutionScopes
{
    public const string Worker = "Worker";
    public const string AppRunner = "AppRunner";
    public const string InfrastructureRunner = "InfrastructureRunner";
}

public static class ActionScheduleConcurrencyPolicies
{
    public const string Allow = "Allow";
    public const string SkipIfRunning = "SkipIfRunning";
    public const string Queue = "Queue";
}

public static class ActionScheduleMisfirePolicies
{
    public const string Skip = "Skip";
    public const string RunOnceNow = "RunOnceNow";
    public const string CatchUp = "CatchUp";
}

public sealed class ActionSchedule
{
    public Guid Id { get; set; }
    public string OwnerType { get; set; } = ActionScheduleOwnerTypes.User;
    public Guid? OwnerUserId { get; set; }
    public Guid ApplicationId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ExecutionScope { get; set; } = ActionExecutionScopes.AppRunner;
    public string ScheduleType { get; set; } = ActionScheduleTypes.Interval;
    public DateTime? RunAtUtc { get; set; }
    public int? IntervalSeconds { get; set; }
    public string? CronExpression { get; set; }
    public string TimeZone { get; set; } = "Etc/UTC";
    public bool Enabled { get; set; } = true;
    public DateTime? NextRunAtUtc { get; set; }
    public DateTime? LastTriggeredAtUtc { get; set; }
    public Guid? LastExecutionId { get; set; }
    public string ConcurrencyPolicy { get; set; } = ActionScheduleConcurrencyPolicies.SkipIfRunning;
    public string MisfirePolicy { get; set; } = ActionScheduleMisfirePolicies.Skip;
    public string InputJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
