using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Mcp.SystemPrompts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

public sealed class OrganizationAiPolicySettingsService(ApplicationDbContext dbContext)
{
    public async Task<(string? DefaultStrategy, bool AllowUserOverride)> GetVerificationPolicyAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var strategy = await ReadJsonSettingAsync<string>(
            VerificationDefaultStrategyKey(organizationId),
            cancellationToken);
        var allowOverride = await ReadJsonSettingAsync<bool?>(
            VerificationAllowUserOverrideKey(organizationId),
            cancellationToken);

        return (NormalizeVerificationStrategy(strategy), allowOverride ?? true);
    }

    public async Task<(string? DefaultPolicy, bool AllowUserOverride)> GetExecutionPolicyAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var policy = await ReadJsonSettingAsync<string>(ExecutionDefaultPolicyKey(organizationId), cancellationToken);
        var allowOverride = await ReadJsonSettingAsync<bool?>(ExecutionAllowUserOverrideKey(organizationId), cancellationToken);

        return (NormalizeExecutionPolicy(policy), allowOverride ?? true);
    }

    public async Task ApplyAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        await UpsertJsonSettingAsync(
            VerificationDefaultStrategyKey(organizationId),
            NormalizeVerificationStrategy(request.AppVerificationDefaultStrategy),
            "Organization-level default app verification strategy",
            cancellationToken);
        await UpsertJsonSettingAsync(
            VerificationAllowUserOverrideKey(organizationId),
            request.AppVerificationAllowUserOverride,
            "Organization-level toggle for allowing user verification strategy overrides",
            cancellationToken);
        await UpsertJsonSettingAsync(
            ExecutionDefaultPolicyKey(organizationId),
            NormalizeExecutionPolicy(request.AppExecutionDefaultPolicy),
            "Organization-level default app execution policy",
            cancellationToken);
        await UpsertJsonSettingAsync(
            ExecutionAllowUserOverrideKey(organizationId),
            request.AppExecutionAllowUserOverride,
            "Organization-level toggle for allowing user execution policy overrides",
            cancellationToken);
    }

    private async Task<T?> ReadJsonSettingAsync<T>(string key, CancellationToken cancellationToken)
    {
        var setting = await dbContext.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (setting is null)
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(setting.Value);
        }
        catch
        {
            return default;
        }
    }

    private async Task UpsertJsonSettingAsync<T>(
        string key,
        T value,
        string description,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Settings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        var json = JsonSerializer.Serialize(value);
        if (existing is null)
        {
            dbContext.Settings.Add(new Setting
            {
                Key = key,
                Value = json,
                Description = description,
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        existing.Value = json;
        existing.Description = description;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    private static string VerificationDefaultStrategyKey(Guid organizationId) =>
        $"organization:{organizationId}:ai.verification.defaultStrategy";

    private static string VerificationAllowUserOverrideKey(Guid organizationId) =>
        $"organization:{organizationId}:ai.verification.allowUserOverride";

    private static string ExecutionDefaultPolicyKey(Guid organizationId) =>
        $"organization:{organizationId}:ai.execution.defaultPolicy";

    private static string ExecutionAllowUserOverrideKey(Guid organizationId) =>
        $"organization:{organizationId}:ai.execution.allowUserOverride";

    private static string? NormalizeVerificationStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
            return null;
        if (strategy.Equals(AppVerificationStrategyNames.Continuous, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.Continuous;
        if (strategy.Equals(AppVerificationStrategyNames.FinalGate, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.FinalGate;
        if (strategy.Equals(AppVerificationStrategyNames.Balanced, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.Balanced;
        return null;
    }

    private static string? NormalizeExecutionPolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
            return null;
        if (policy.Equals(AppExecutionPolicyNames.LeanTools, StringComparison.OrdinalIgnoreCase))
            return AppExecutionPolicyNames.LeanTools;
        if (policy.Equals(AppExecutionPolicyNames.Default, StringComparison.OrdinalIgnoreCase))
            return AppExecutionPolicyNames.Default;
        return null;
    }
}
