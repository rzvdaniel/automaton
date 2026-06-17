using Automaton.Api.Data;
using Automaton.Mcp.SystemPrompts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

public interface IAppVerificationStrategyResolver
{
    Task<string> ResolveEffectiveStrategyAsync(Guid userId, Guid? appId, CancellationToken cancellationToken = default);
    Task<string> ResolveEffectiveExecutionPolicyAsync(Guid userId, Guid? appId, CancellationToken cancellationToken = default);
}

public static class AppVerificationStrategySettingKeys
{
    public static string OrganizationDefaultStrategy(Guid organizationId) =>
        $"organization:{organizationId}:ai.verification.defaultStrategy";

    public static string OrganizationAllowUserOverride(Guid organizationId) =>
        $"organization:{organizationId}:ai.verification.allowUserOverride";

    public static string OrganizationDefaultExecutionPolicy(Guid organizationId) =>
        $"organization:{organizationId}:ai.execution.defaultPolicy";

    public static string OrganizationAllowUserExecutionOverride(Guid organizationId) =>
        $"organization:{organizationId}:ai.execution.allowUserOverride";
}

public class AppVerificationStrategyResolver : IAppVerificationStrategyResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserSettingsService _userSettingsService;
    private readonly IAiProvidersSettingsService _aiProvidersSettingsService;

    public AppVerificationStrategyResolver(
        ApplicationDbContext dbContext,
        UserSettingsService userSettingsService,
        IAiProvidersSettingsService aiProvidersSettingsService)
    {
        _dbContext = dbContext;
        _userSettingsService = userSettingsService;
        _aiProvidersSettingsService = aiProvidersSettingsService;
    }

    public async Task<string> ResolveEffectiveStrategyAsync(
        Guid userId,
        Guid? appId,
        CancellationToken cancellationToken = default)
    {
        var globalSettings = await _aiProvidersSettingsService.GetOperationalSettingsAsync();
        var globalDefault = NormalizeStrategy(globalSettings.AppVerificationDefaultStrategy);
        var globalAllowUserOverride = globalSettings.AppVerificationAllowUserOverride;

        var userPreferenceRaw = await _userSettingsService.GetSettingAsync<string?>(
            userId,
            UserSettingsService.Keys.AppVerificationStrategy,
            null);
        var userPreference = string.IsNullOrWhiteSpace(userPreferenceRaw)
            ? null
            : NormalizeStrategy(userPreferenceRaw);

        // Non-organization users: user preference first (if allowed), then global default.
        if (!appId.HasValue)
        {
            return ResolveWithOverride(globalDefault, globalAllowUserOverride, userPreference);
        }

        var organizationId = await ResolveOrganizationIdAsync(appId.Value, cancellationToken);
        if (!organizationId.HasValue)
        {
            return ResolveWithOverride(globalDefault, globalAllowUserOverride, userPreference);
        }

        // Safety: only apply org policy when the user is actually an active member.
        var isMember = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(
                m => m.OrganizationId == organizationId.Value &&
                     m.UserId == userId &&
                     m.IsActive,
                cancellationToken);

        if (!isMember)
        {
            return ResolveWithOverride(globalDefault, globalAllowUserOverride, userPreference);
        }

        var orgDefault = await GetOrganizationDefaultStrategyAsync(organizationId.Value, cancellationToken)
            ?? globalDefault;

        var orgAllowUserOverride = await GetOrganizationAllowUserOverrideAsync(organizationId.Value, cancellationToken)
            ?? globalAllowUserOverride;

        return ResolveWithOverride(orgDefault, orgAllowUserOverride, userPreference);
    }

    public async Task<string> ResolveEffectiveExecutionPolicyAsync(
        Guid userId,
        Guid? appId,
        CancellationToken cancellationToken = default)
    {
        var globalSettings = await _aiProvidersSettingsService.GetOperationalSettingsAsync();
        var globalDefault = NormalizeExecutionPolicy(globalSettings.AppExecutionDefaultPolicy);
        var globalAllowUserOverride = globalSettings.AppExecutionAllowUserOverride;

        var userPreferenceRaw = await _userSettingsService.GetSettingAsync<string?>(
            userId,
            UserSettingsService.Keys.AppExecutionPolicy,
            null);
        var userPreference = string.IsNullOrWhiteSpace(userPreferenceRaw)
            ? null
            : NormalizeExecutionPolicy(userPreferenceRaw);

        if (!appId.HasValue)
        {
            return ResolveWithOverride(globalDefault, globalAllowUserOverride, userPreference);
        }

        var organizationId = await ResolveOrganizationIdAsync(appId.Value, cancellationToken);
        if (!organizationId.HasValue)
        {
            return ResolveWithOverride(globalDefault, globalAllowUserOverride, userPreference);
        }

        var isMember = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(
                m => m.OrganizationId == organizationId.Value &&
                     m.UserId == userId &&
                     m.IsActive,
                cancellationToken);

        if (!isMember)
        {
            return ResolveWithOverride(globalDefault, globalAllowUserOverride, userPreference);
        }

        var orgDefault = await GetOrganizationDefaultExecutionPolicyAsync(organizationId.Value, cancellationToken)
            ?? globalDefault;

        var orgAllowUserOverride = await GetOrganizationAllowUserExecutionOverrideAsync(organizationId.Value, cancellationToken)
            ?? globalAllowUserOverride;

        return ResolveWithOverride(orgDefault, orgAllowUserOverride, userPreference);
    }

    private async Task<Guid?> ResolveOrganizationIdAsync(Guid appId, CancellationToken cancellationToken)
    {
        var projectId = await _dbContext.WebApps
            .AsNoTracking()
            .Where(a => a.Id == appId)
            .Select(a => a.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!projectId.HasValue)
            return null;

        return await _dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId.Value && p.IsActive)
            .Select(p => p.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> GetOrganizationDefaultStrategyAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var key = AppVerificationStrategySettingKeys.OrganizationDefaultStrategy(organizationId);
        var value = await _dbContext.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return NormalizeStrategy(JsonSerializer.Deserialize<string>(value));
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool?> GetOrganizationAllowUserOverrideAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var key = AppVerificationStrategySettingKeys.OrganizationAllowUserOverride(organizationId);
        var value = await _dbContext.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<bool>(value);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> GetOrganizationDefaultExecutionPolicyAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var key = AppVerificationStrategySettingKeys.OrganizationDefaultExecutionPolicy(organizationId);
        var value = await _dbContext.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return NormalizeExecutionPolicy(JsonSerializer.Deserialize<string>(value));
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool?> GetOrganizationAllowUserExecutionOverrideAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var key = AppVerificationStrategySettingKeys.OrganizationAllowUserExecutionOverride(organizationId);
        var value = await _dbContext.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<bool>(value);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveWithOverride(string @default, bool allowUserOverride, string? userPreference)
    {
        if (allowUserOverride && !string.IsNullOrWhiteSpace(userPreference))
            return userPreference;

        return @default;
    }

    private static string NormalizeStrategy(string? value)
    {
        var candidate = value?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
            return AppVerificationStrategyNames.Balanced;

        if (candidate.Equals(AppVerificationStrategyNames.Continuous, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.Continuous;

        if (candidate.Equals(AppVerificationStrategyNames.FinalGate, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.FinalGate;

        return AppVerificationStrategyNames.Balanced;
    }

    private static string NormalizeExecutionPolicy(string? value)
    {
        var candidate = value?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
            return AppExecutionPolicyNames.Default;

        if (candidate.Equals(AppExecutionPolicyNames.LeanTools, StringComparison.OrdinalIgnoreCase))
            return AppExecutionPolicyNames.LeanTools;

        return AppExecutionPolicyNames.Default;
    }
}
