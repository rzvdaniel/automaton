using System.Text.RegularExpressions;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AppSecretSummary(string Name, int Version, DateTime UpdatedAtUtc);

/// <summary>
/// Encrypted per-app secrets, delivered to the app's Actions as environment variables.
/// Values are write-only: they are never returned by the API after saving.
/// </summary>
public sealed partial class AppSecretService(
    ApplicationDbContext db,
    ISecretStore store,
    ISecretProtector protector,
    ResourceAuthorizationService authorization)
{
    public const int MaxSecretsPerApp = 50;
    public const int MaxValueLength = 16 * 1024;

    public async Task<IReadOnlyList<AppSecretSummary>> ListAsync(Guid appId, Guid userId, CancellationToken token)
    {
        await EnsureCanManageAsync(appId, userId, token);
        return await Definitions(appId)
            .OrderBy(secret => secret.Name)
            .Select(secret => new AppSecretSummary(secret.Name, secret.CurrentVersion, secret.UpdatedAtUtc))
            .ToListAsync(token);
    }

    public async Task<AppSecretSummary> SetAsync(Guid appId, Guid userId, string name, string value, CancellationToken token)
    {
        await EnsureCanManageAsync(appId, userId, token);
        if (!NamePattern().IsMatch(name) || name.StartsWith("AUTOMATON_", StringComparison.Ordinal))
            throw new ArgumentException("Use an environment variable name of capital letters, digits and underscores, not starting with a digit or AUTOMATON_.", nameof(name));
        if (string.IsNullOrEmpty(value) || value.Length > MaxValueLength)
            throw new ArgumentException($"A secret value must be 1 to {MaxValueLength} characters.", nameof(value));
        var exists = await Definitions(appId).AnyAsync(secret => secret.Name == name, token);
        if (!exists && await Definitions(appId).CountAsync(token) >= MaxSecretsPerApp)
            throw new ArgumentException($"An app can have at most {MaxSecretsPerApp} secrets.", nameof(name));

        var created = await store.CreateVersionAsync(new SecretOwner(SecretOwnerTypes.App, appId), name, value, null, token);
        // Only the newest version is delivered; earlier versions are revoked.
        var earlier = await db.SecretVersions
            .Where(version => version.SecretDefinitionId == created.SecretDefinitionId && version.Id != created.SecretVersionId && version.RevokedAtUtc == null)
            .ToListAsync(token);
        foreach (var version in earlier) version.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        return new(name, created.Version, DateTime.UtcNow);
    }

    public async Task<bool> DeleteAsync(Guid appId, Guid userId, string name, CancellationToken token)
    {
        await EnsureCanManageAsync(appId, userId, token);
        var definition = await Definitions(appId).Include(secret => secret.Versions).SingleOrDefaultAsync(secret => secret.Name == name, token);
        if (definition is null) return false;
        db.SecretVersions.RemoveRange(definition.Versions);
        db.SecretDefinitions.Remove(definition);
        await db.SaveChangesAsync(token);
        return true;
    }

    /// <summary>Current secret values for an Action run. Called only by the platform when dispatching.</summary>
    public async Task<Dictionary<string, string>> ResolveForExecutionAsync(Guid appId, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var current = await Definitions(appId)
            .SelectMany(secret => secret.Versions
                .Where(version => version.Version == secret.CurrentVersion && version.RevokedAtUtc == null
                    && (version.ExpiresAtUtc == null || version.ExpiresAtUtc > now))
                .Select(version => new { secret.Name, version.ProtectedValue }))
            .ToListAsync(token);
        return current.ToDictionary(secret => secret.Name, secret => protector.Unprotect(secret.ProtectedValue), StringComparer.Ordinal);
    }

    /// <summary>Stages removal of every secret of an app; the caller saves with the app deletion.</summary>
    public static async Task RemoveAllForAppAsync(ApplicationDbContext db, Guid appId, CancellationToken token)
    {
        var definitions = await db.SecretDefinitions.Include(secret => secret.Versions)
            .Where(secret => secret.OwnerType == SecretOwnerTypes.App && secret.OwnerId == appId)
            .ToListAsync(token);
        db.SecretVersions.RemoveRange(definitions.SelectMany(secret => secret.Versions));
        db.SecretDefinitions.RemoveRange(definitions);
    }

    private IQueryable<SecretDefinition> Definitions(Guid appId)
        => db.SecretDefinitions.Where(secret => secret.OwnerType == SecretOwnerTypes.App && secret.OwnerId == appId && secret.DeletedAtUtc == null);

    private async Task EnsureCanManageAsync(Guid appId, Guid userId, CancellationToken token)
    {
        if (!await authorization.CanUpdateAppAsync(appId, userId, token))
            throw new UnauthorizedAccessException("You do not have permission to manage this app's secrets.");
    }

    [GeneratedRegex("^[A-Z_][A-Z0-9_]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
