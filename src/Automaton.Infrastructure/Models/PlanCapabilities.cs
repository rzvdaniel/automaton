using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models;

public sealed class PlanCapabilities
{
    public int Version { get; set; } = 1;
    public ScheduledActionsCapability ScheduledActions { get; set; } = new();

    public static PlanCapabilities Default => new();
}

public sealed class ScheduledActionsCapability
{
    public bool Enabled { get; set; } = true;
    public int MaximumSchedules { get; set; } = 10;
    public int MinimumIntervalSeconds { get; set; } = 300;
    public int MaximumConcurrentExecutions { get; set; } = 1;
    public int MonthlyExecutionLimit { get; set; } = 5000;
}

public static class SubscriptionPlanIds
{
    public static readonly Guid Free = new("6f03b7e0-3d96-4e51-8c49-1a4c03d7f4f7");
}

public static class PlanCapabilitiesSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static PlanCapabilities Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return PlanCapabilities.Default;

        try
        {
            var capabilities = JsonSerializer.Deserialize<PlanCapabilities>(json, JsonOptions)
                ?? PlanCapabilities.Default;
            Normalize(capabilities);
            return capabilities;
        }
        catch (JsonException)
        {
            return PlanCapabilities.Default;
        }
    }

    public static string Serialize(PlanCapabilities? capabilities)
    {
        capabilities ??= PlanCapabilities.Default;
        Normalize(capabilities);
        return JsonSerializer.Serialize(capabilities, JsonOptions);
    }

    public static void Normalize(PlanCapabilities capabilities)
    {
        capabilities.Version = capabilities.Version <= 0 ? 1 : capabilities.Version;
        capabilities.ScheduledActions ??= new ScheduledActionsCapability();

        var schedules = capabilities.ScheduledActions;
        schedules.MaximumSchedules = schedules.MaximumSchedules > 0 ? schedules.MaximumSchedules : 10;
        schedules.MinimumIntervalSeconds = schedules.MinimumIntervalSeconds > 0 ? schedules.MinimumIntervalSeconds : 300;
        schedules.MaximumConcurrentExecutions = schedules.MaximumConcurrentExecutions > 0 ? schedules.MaximumConcurrentExecutions : 1;
        schedules.MonthlyExecutionLimit = schedules.MonthlyExecutionLimit > 0 ? schedules.MonthlyExecutionLimit : 5000;
    }

    public static bool HasLegacyHostedRunnerEntitlement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("hostedRunner", out var hostedRunner)
                && hostedRunner.ValueKind == JsonValueKind.Object
                && hostedRunner.TryGetProperty("enabled", out var enabled)
                && enabled.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
