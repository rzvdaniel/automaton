using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AppSettingValueSummary(string Name, string Value, DateTime UpdatedAtUtc);

/// <summary>
/// Readable values for the plain settings an app declares in its contract. Only names declared under
/// <c>settings</c> (and not under <c>secrets</c>) can be saved or read here; the write-only secret store is
/// never read for display. Saving a value also removes the name's older override in the secret store.
/// </summary>
public sealed class AppSettingValueService(
    ApplicationDbContext db,
    AppSecretService secrets,
    ResourceAuthorizationService authorization,
    IAppRuntimeRestarter? runtimes = null)
{
    public async Task<IReadOnlyList<AppSettingValueSummary>> ListAsync(Guid appId, Guid userId, CancellationToken token)
    {
        await EnsureCanUpdateAppAsync(appId, userId, token);
        var names = await DeclaredSettingNamesAsync(appId, token);
        var rows = await db.AppSettingValues.Where(setting => setting.AppId == appId && names.Contains(setting.Name))
            .OrderBy(setting => setting.Name).ToListAsync(token);
        return rows.Select(setting => new AppSettingValueSummary(setting.Name, setting.Value, setting.UpdatedAtUtc)).ToList();
    }

    public async Task<AppSettingValueSummary> SetAsync(Guid appId, Guid userId, string name, string value, CancellationToken token)
    {
        await EnsureCanUpdateAppAsync(appId, userId, token);
        if (!(await DeclaredSettingNamesAsync(appId, token)).Contains(name))
            throw new ArgumentException($"{name} is not a setting this app declares.", nameof(name));
        if (string.IsNullOrEmpty(value) || value.Length > AppSecretService.MaxValueLength)
            throw new ArgumentException($"A setting value must be 1 to {AppSecretService.MaxValueLength} characters.", nameof(value));
        var row = await db.AppSettingValues.SingleOrDefaultAsync(setting => setting.AppId == appId && setting.Name == name, token);
        if (row is null)
            db.AppSettingValues.Add(row = new() { Id = Guid.NewGuid(), AppId = appId, Name = name });
        row.Value = value;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedByUserId = userId;
        await db.SaveChangesAsync(token);
        await secrets.DeleteAsync(appId, userId, name, token);
        await RestartAsync(appId);
        return new(name, value, row.UpdatedAtUtc);
    }

    /// <summary>Goes back to the declared default: removes the saved value and any older override in the secret store.</summary>
    public async Task<bool> ClearAsync(Guid appId, Guid userId, string name, CancellationToken token)
    {
        await EnsureCanUpdateAppAsync(appId, userId, token);
        var row = await db.AppSettingValues.SingleOrDefaultAsync(setting => setting.AppId == appId && setting.Name == name, token);
        if (row is not null)
        {
            db.AppSettingValues.Remove(row);
            await db.SaveChangesAsync(token);
        }
        var oldOverride = await secrets.DeleteAsync(appId, userId, name, token);
        if (row is not null || oldOverride) await RestartAsync(appId);
        return row is not null || oldOverride;
    }

    /// <summary>Running copies keep the old value until restarted, so a saved setting restarts them (queued, not awaited).</summary>
    private async Task RestartAsync(Guid appId)
    {
        if (runtimes is not null) await runtimes.RestartRunningAsync(appId, CancellationToken.None);
    }

    private async Task<HashSet<string>> DeclaredSettingNamesAsync(Guid appId, CancellationToken token)
    {
        var contract = await secrets.ContractAsync(appId, token);
        var declaredSecrets = (contract.Secrets ?? []).Select(secret => secret.Name).ToHashSet(StringComparer.Ordinal);
        return (contract.Settings ?? []).Select(setting => setting.Name).Where(name => !declaredSecrets.Contains(name)).ToHashSet(StringComparer.Ordinal);
    }

    private async Task EnsureCanUpdateAppAsync(Guid appId, Guid userId, CancellationToken token)
    {
        if (!await authorization.CanUpdateAppAsync(appId, userId, token))
            throw new UnauthorizedAccessException("You do not have permission to change this app's settings.");
    }
}
