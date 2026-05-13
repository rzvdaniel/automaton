using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Studio.Server.Models;

public sealed class PlanCapabilities
{
    public int Version { get; set; } = 1;
    public HostedRunnerCapability HostedRunner { get; set; } = new();

    public static PlanCapabilities Default => new();
}

public sealed class HostedRunnerCapability
{
    public bool Enabled { get; set; }
    public decimal CpuCores { get; set; } = 0.5m;
    public int MemoryMb { get; set; } = 768;
    public int DiskMb { get; set; } = 2048;
    public int MaxApps { get; set; } = 10;
    public string Visibility { get; set; } = HostedRunnerVisibility.Disabled;
}

public static class HostedRunnerVisibility
{
    public const string Disabled = "disabled";
    public const string TestUsersOnly = "testUsersOnly";
    public const string Public = "public";

    public static bool IsValid(string? value) => value is Disabled or TestUsersOnly or Public;
}

public static class HostedRunnerPlanVisibility
{
    public static bool IsVisibleInCatalog(PlanCapabilities capabilities, bool canAccessHostedRunnerTestPlans)
    {
        var visibility = capabilities.HostedRunner.Visibility;
        return visibility == HostedRunnerVisibility.Public
            || (visibility == HostedRunnerVisibility.TestUsersOnly && canAccessHostedRunnerTestPlans);
    }
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
        capabilities.HostedRunner ??= new HostedRunnerCapability();

        var runner = capabilities.HostedRunner;
        runner.CpuCores = runner.CpuCores <= 0 ? 0.5m : runner.CpuCores;
        runner.MemoryMb = runner.MemoryMb <= 0 ? 768 : runner.MemoryMb;
        runner.DiskMb = runner.DiskMb <= 0 ? 2048 : runner.DiskMb;
        runner.MaxApps = runner.MaxApps <= 0 ? 10 : runner.MaxApps;
        runner.Visibility = HostedRunnerVisibility.IsValid(runner.Visibility)
            ? runner.Visibility
            : HostedRunnerVisibility.Disabled;
    }
}
