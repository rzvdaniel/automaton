using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Retention;

/// <summary>Server-wide retention defaults, kept as one JSON value in the Settings table.</summary>
public sealed record RetentionDefaults
{
    public int RunDays { get; init; } = 30;
    public int MinimumRunsPerAction { get; init; } = 20;
    public int LogDays { get; init; } = 7;
    /// <summary>Failed runs, and their logs, are kept this many times as long as successful ones.</summary>
    public int FailedMultiplier { get; init; } = 2;
    public int AuditDays { get; init; } = 365;
    public int HomeCardRunsToKeep { get; init; } = 3;
}

/// <summary>A per-app override; unset values fall back to the server defaults.</summary>
public sealed record RetentionAppOverride
{
    public int? RunDays { get; init; }
    public int? MinimumRunsPerAction { get; init; }
    public int? LogDays { get; init; }
}

public sealed record EffectiveRetention(int RunDays, int MinimumRunsPerAction, int LogDays, int FailedMultiplier);

/// <summary>Reads and validates retention settings. Run and log retention over <see cref="ApprovalThresholdDays"/> days needs the admin's approval.</summary>
public static class RetentionSettings
{
    public const string DefaultsKey = "Retention.Defaults";
    public const string AppKeyPrefix = "Retention.App.";
    public const int ApprovalThresholdDays = 90;
    public const int MaximumDays = 3650;
    /// <summary>Audit events are never kept shorter than this, so a short setting cannot erase the record of its own change.</summary>
    public const int MinimumAuditDays = 30;
    public const string HomeCardAction = "home-card";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Entity types of secret audit events; <c>SecretAuditLog</c> prunes these after 180 days on its own.</summary>
    public static readonly string[] SecretAuditEntityTypes = ["AppSecretVault", "ProjectSecretVault", "UserSecretVault"];

    public static string AppKey(Guid appId) => AppKeyPrefix + appId.ToString("D");

    public static async Task<RetentionDefaults> GetDefaultsAsync(ApplicationDbContext db, CancellationToken token)
        => Read<RetentionDefaults>(await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Key == DefaultsKey, token)) ?? new();

    public static async Task<RetentionAppOverride?> GetAppOverrideAsync(ApplicationDbContext db, Guid appId, CancellationToken token)
        => Read<RetentionAppOverride>(await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Key == AppKey(appId), token));

    public static Task SaveDefaultsAsync(ApplicationDbContext db, RetentionDefaults value, bool approved, CancellationToken token)
    {
        CheckDays("RunDays", value.RunDays, approved, needsApproval: true);
        CheckDays("LogDays", value.LogDays, approved, needsApproval: true);
        CheckDays("AuditDays", value.AuditDays, approved, needsApproval: false, minimum: MinimumAuditDays);
        CheckMinimum(value.MinimumRunsPerAction);
        if (value.FailedMultiplier is < 1 or > 10) throw new ArgumentException("FailedMultiplier must be between 1 and 10.");
        if (value.HomeCardRunsToKeep is < 1 or > 100) throw new ArgumentException("HomeCardRunsToKeep must be between 1 and 100.");
        return SaveAsync(db, DefaultsKey, value, "Server retention for Action runs, logs and audit events", token);
    }

    /// <summary>Saves the override, or removes it when <paramref name="value"/> is null or empty.</summary>
    public static async Task SaveAppOverrideAsync(ApplicationDbContext db, Guid appId, RetentionAppOverride? value, bool approved, CancellationToken token)
    {
        if (value is null || value is { RunDays: null, MinimumRunsPerAction: null, LogDays: null })
        {
            db.Settings.RemoveRange(await db.Settings.Where(x => x.Key == AppKey(appId)).ToListAsync(token));
            await db.SaveChangesAsync(token);
            return;
        }
        if (value.RunDays is { } runDays) CheckDays("RunDays", runDays, approved, needsApproval: true);
        if (value.LogDays is { } logDays) CheckDays("LogDays", logDays, approved, needsApproval: true);
        if (value.MinimumRunsPerAction is { } minimum) CheckMinimum(minimum);
        await SaveAsync(db, AppKey(appId), value, "App retention override", token);
    }

    public static EffectiveRetention Resolve(RetentionDefaults defaults, RetentionAppOverride? app)
        => new(app?.RunDays ?? defaults.RunDays, app?.MinimumRunsPerAction ?? defaults.MinimumRunsPerAction,
            app?.LogDays ?? defaults.LogDays, defaults.FailedMultiplier);

    private static void CheckDays(string name, int days, bool approved, bool needsApproval, int minimum = 1)
    {
        if (days < minimum || days > MaximumDays) throw new ArgumentException($"{name} must be between {minimum} and {MaximumDays}.");
        if (needsApproval && days > ApprovalThresholdDays && !approved)
            throw new ArgumentException($"Keeping {name} more than {ApprovalThresholdDays} days needs approval.");
    }

    private static void CheckMinimum(int value)
    {
        if (value is < 0 or > 1000) throw new ArgumentException("MinimumRunsPerAction must be between 0 and 1000.");
    }

    private static async Task SaveAsync<T>(ApplicationDbContext db, string key, T value, string description, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(value, Json);
        var row = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, token);
        if (row is null) db.Settings.Add(new Setting { Key = key, Value = json, Description = description, UpdatedAt = DateTime.UtcNow });
        else { row.Value = json; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(token);
    }

    private static T? Read<T>(Setting? row) where T : class
    {
        if (row is null) return null;
        try { return JsonSerializer.Deserialize<T>(row.Value, Json); }
        catch (JsonException) { return null; }
    }
}
