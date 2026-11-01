using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AppSecretSummary(string Name, int Version, DateTime UpdatedAtUtc, string Scope = AppSecretScopes.App, string? Description = null);

/// <summary>One declared secret: whether it is filled and which scope provides it. Never carries a value.</summary>
public sealed record AppSecretStatus(string Name, string? Description, string? From, bool Present, string? ProvidedBy, string? Problem);

/// <summary>Where a secret lives. Every scope is addressed relative to an app.</summary>
public static class AppSecretScopes
{
    public const string App = "app";
    public const string Project = ApplicationSecretNames.ProjectScope;
    public const string User = ApplicationSecretNames.UserScope;
}

/// <summary>
/// Encrypted secrets for apps: the app's own secrets plus a shared vault per project and per user.
/// An app receives all of its own secrets, and vault secrets only for names it declares in
/// <c>application.secrets</c>. Values are write-only: they are never returned after saving.
/// </summary>
public sealed class AppSecretService(
    ApplicationDbContext db,
    ISecretStore store,
    ISecretProtector protector,
    ResourceAuthorizationService authorization)
{
    public const int MaxSecretsPerApp = 50;
    public const int MaxSecretsPerVault = 200;
    public const int MaxValueLength = 16 * 1024;

    public Task<IReadOnlyList<AppSecretSummary>> ListAsync(Guid appId, Guid userId, CancellationToken token)
        => ListAsync(appId, userId, AppSecretScopes.App, token);

    public async Task<IReadOnlyList<AppSecretSummary>> ListAsync(Guid appId, Guid userId, string scope, CancellationToken token)
    {
        var owner = await OwnerForManagementAsync(appId, userId, scope, write: false, token);
        return await Definitions(owner)
            .OrderBy(secret => secret.Name)
            .Select(secret => new AppSecretSummary(secret.Name, secret.CurrentVersion, secret.UpdatedAtUtc, scope, secret.Description))
            .ToListAsync(token);
    }

    public Task<AppSecretSummary> SetAsync(Guid appId, Guid userId, string name, string value, CancellationToken token)
        => SetAsync(appId, userId, AppSecretScopes.App, name, value, null, token);

    public async Task<AppSecretSummary> SetAsync(Guid appId, Guid userId, string scope, string name, string value,
        string? description, CancellationToken token)
    {
        var owner = await OwnerForManagementAsync(appId, userId, scope, write: true, token);
        description = ValidateInput(name, value, description);
        var limit = scope == AppSecretScopes.App ? MaxSecretsPerApp : MaxSecretsPerVault;
        var exists = await Definitions(owner).AnyAsync(secret => secret.Name == name, token);
        if (!exists && await Definitions(owner).CountAsync(token) >= limit)
            throw new ArgumentException($"At most {limit} secrets are allowed here.", nameof(name));

        var created = await store.CreateVersionAsync(owner, name, value, null, token);
        // Only the newest version is delivered; earlier versions are revoked.
        var earlier = await db.SecretVersions
            .Where(version => version.SecretDefinitionId == created.SecretDefinitionId && version.Id != created.SecretVersionId && version.RevokedAtUtc == null)
            .ToListAsync(token);
        foreach (var version in earlier) version.RevokedAtUtc = DateTime.UtcNow;
        if (description is not null)
        {
            var definition = await db.SecretDefinitions.SingleAsync(secret => secret.Id == created.SecretDefinitionId, token);
            definition.Description = description;
        }
        await db.SaveChangesAsync(token);
        return new(name, created.Version, DateTime.UtcNow, scope, description);
    }

    private static string? ValidateInput(string name, string value, string? description)
    {
        if (!ApplicationSecretNames.IsValidName(name))
            throw new ArgumentException("Use an environment variable name of capital letters, digits and underscores, not starting with a digit or AUTOMATON_.", nameof(name));
        if (string.IsNullOrEmpty(value) || value.Length > MaxValueLength)
            throw new ArgumentException($"A secret value must be 1 to {MaxValueLength} characters.", nameof(value));
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description is not null && (description.Length > ApplicationSecretNames.MaxDescriptionLength || description.Any(char.IsControl)))
            throw new ArgumentException($"A description is one line of at most {ApplicationSecretNames.MaxDescriptionLength} characters.", nameof(description));
        return description;
    }

    public Task<bool> DeleteAsync(Guid appId, Guid userId, string name, CancellationToken token)
        => DeleteAsync(appId, userId, AppSecretScopes.App, name, token);

    public async Task<bool> DeleteAsync(Guid appId, Guid userId, string scope, string name, CancellationToken token)
    {
        var owner = await OwnerForManagementAsync(appId, userId, scope, write: true, token);
        var definition = await Definitions(owner).Include(secret => secret.Versions).SingleOrDefaultAsync(secret => secret.Name == name, token);
        if (definition is null) return false;
        db.SecretVersions.RemoveRange(definition.Versions);
        db.SecretDefinitions.Remove(definition);
        await db.SaveChangesAsync(token);
        return true;
    }

    /// <summary>For each declaration, whether a value is available to the app and from which scope.</summary>
    public async Task<IReadOnlyList<AppSecretStatus>> StatusAsync(Guid appId, Guid userId,
        IReadOnlyList<ApplicationSecretDeclaration> declarations, CancellationToken token)
    {
        await EnsureCanUpdateAppAsync(appId, userId, token);
        var contract = new ApplicationContract { Secrets = declarations.ToList() };
        if (ApplicationContractValidator.Validate(contract).FirstOrDefault(error => error.StartsWith("secrets", StringComparison.Ordinal)) is { } invalid)
            throw new ArgumentException(invalid, nameof(declarations));
        var vaults = await VaultsAsync(appId, token);
        var available = await CurrentNamesAsync(vaults, token);
        return declarations.Select(declaration =>
        {
            var (providedBy, problem) = Locate(declaration, vaults, available);
            return new AppSecretStatus(declaration.Name, declaration.Description, declaration.From, providedBy is not null, providedBy, problem);
        }).ToList();
    }

    /// <summary>
    /// Current secret values for an Action run or runtime start: every app secret, plus vault secrets for
    /// declared names the app does not have itself. Called only by the platform when dispatching.
    /// </summary>
    public async Task<Dictionary<string, string>> ResolveForExecutionAsync(Guid appId, CancellationToken token)
    {
        var vaults = await VaultsAsync(appId, token);
        var values = await CurrentValuesAsync(vaults.App, null, token);
        var declarations = await DeclarationsAsync(appId, token);
        var wanted = declarations.Where(declaration => !values.ContainsKey(declaration.Name)).ToList();
        var available = await CurrentNamesAsync(vaults, token);
        var sources = new List<(string Name, SecretOwner Owner, string SourceName)>();
        foreach (var declaration in wanted)
            if (Source(declaration, vaults, available) is { } source)
                sources.Add((declaration.Name, source.Owner, source.Name));
        foreach (var group in sources.GroupBy(source => source.Owner))
        {
            var groupValues = await CurrentValuesAsync(group.Key, group.Select(source => source.SourceName).ToHashSet(StringComparer.Ordinal), token);
            foreach (var source in group)
                if (groupValues.TryGetValue(source.SourceName, out var value))
                    values[source.Name] = value;
        }
        var permissionCredentials = await db.AppPermissionApprovals
            .Where(approval => approval.AppId == appId && approval.Permission == "backups:read" && approval.ProtectedKey != null)
            .Select(approval => approval.ProtectedKey).ToListAsync(token);
        if (permissionCredentials.Count > 0)
            values["APP_AUTOMATON_API_KEY"] = protector.Unprotect(permissionCredentials[0]!);
        return values;
    }

    /// <summary>Current secret values for a runtime start, only for a target assigned to the requesting Runner.</summary>
    public async Task<Dictionary<string, string>> ResolveForRuntimeAsync(
        Guid appId, Guid deploymentTargetId, Guid runnerId, CancellationToken token)
    {
        var assigned = await db.AppDeploymentTargets.AnyAsync(target =>
            target.Id == deploymentTargetId && target.WebAppId == appId && target.ExecutionRunnerId == runnerId, token);
        if (!assigned)
            throw new UnauthorizedAccessException("The deployment target is not assigned to this Runner.");
        return await ResolveForExecutionAsync(appId, token);
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

    /// <summary>
    /// The vaults an app may draw from. The project vault applies only to an app in exactly one project;
    /// the user vault only to a personal app (no project, one owner), so nobody who can change the app's
    /// code can pull another person's personal secrets into it.
    /// </summary>
    private sealed record AppVaults(SecretOwner App, SecretOwner? Project, SecretOwner? User, string? ProjectProblem, string? UserProblem);

    private async Task<AppVaults> VaultsAsync(Guid appId, CancellationToken token)
    {
        var projects = await db.ProjectApps.Where(link => link.WebAppId == appId).Select(link => link.ProjectId).Distinct().ToListAsync(token);
        var owners = await db.WebAppUsers.Where(user => user.WebAppId == appId).Select(user => user.UserId).Distinct().ToListAsync(token);
        var project = projects.Count == 1 ? new SecretOwner(SecretOwnerTypes.Project, projects[0]) : null;
        var user = projects.Count == 0 && owners.Count == 1 ? new SecretOwner(SecretOwnerTypes.User, owners[0]) : null;
        return new(new SecretOwner(SecretOwnerTypes.App, appId), project, user,
            projects.Count switch { 0 => "the app is not in a project", 1 => null, _ => "the app is in more than one project" },
            projects.Count > 0 ? "user vaults apply only to personal apps, not project apps"
                : owners.Count == 1 ? null : "user vaults apply only to apps with a single owner");
    }

    private async Task<SecretOwner> OwnerForManagementAsync(Guid appId, Guid userId, string scope, bool write, CancellationToken token)
    {
        await EnsureCanUpdateAppAsync(appId, userId, token);
        switch (scope)
        {
            case AppSecretScopes.App:
                return new(SecretOwnerTypes.App, appId);
            case AppSecretScopes.Project:
                var vaults = await VaultsAsync(appId, token);
                if (vaults.Project is null)
                    throw new ArgumentException($"This app has no project vault: {vaults.ProjectProblem}.", nameof(scope));
                var minimum = write ? ProjectRole.Developer : ProjectRole.Viewer;
                if (!await authorization.HasRoleInProjectAsync(vaults.Project.OwnerId, userId, minimum, token))
                    throw new UnauthorizedAccessException(write
                        ? "Only project administrators and developers can change the project's secrets."
                        : "Only project members can list the project's secrets.");
                return vaults.Project;
            case AppSecretScopes.User:
                // Always the caller's own vault.
                return new(SecretOwnerTypes.User, userId);
            default:
                throw new ArgumentException("The scope must be app, project or user.", nameof(scope));
        }
    }

    private static (string? ProvidedBy, string? Problem) Locate(
        ApplicationSecretDeclaration declaration, AppVaults vaults, IReadOnlyDictionary<SecretOwner, HashSet<string>> available)
    {
        if (available[vaults.App].Contains(declaration.Name)) return (AppSecretScopes.App, null);
        if (Source(declaration, vaults, available) is { } source)
            return (source.Owner.OwnerType == SecretOwnerTypes.Project ? AppSecretScopes.Project : AppSecretScopes.User, null);
        if (ApplicationSecretNames.TryParseFrom(declaration.From, out var scope, out var name))
        {
            var problem = scope == AppSecretScopes.Project ? vaults.ProjectProblem : vaults.UserProblem;
            return (null, problem ?? $"{name} is not set in the {scope} vault");
        }
        return (null, "not set");
    }

    private static (SecretOwner Owner, string Name)? Source(
        ApplicationSecretDeclaration declaration, AppVaults vaults, IReadOnlyDictionary<SecretOwner, HashSet<string>> available)
    {
        if (ApplicationSecretNames.TryParseFrom(declaration.From, out var scope, out var name))
        {
            var owner = scope == AppSecretScopes.Project ? vaults.Project : vaults.User;
            return owner is not null && available[owner].Contains(name) ? (owner, name) : null;
        }
        foreach (var owner in new[] { vaults.Project, vaults.User })
            if (owner is not null && available[owner].Contains(declaration.Name))
                return (owner, declaration.Name);
        return null;
    }

    private async Task<Dictionary<SecretOwner, HashSet<string>>> CurrentNamesAsync(AppVaults vaults, CancellationToken token)
    {
        var result = new Dictionary<SecretOwner, HashSet<string>>();
        foreach (var owner in new[] { vaults.App, vaults.Project, vaults.User })
            if (owner is not null)
                result[owner] = (await CurrentAsync(owner, null, token)).Select(secret => secret.Name).ToHashSet(StringComparer.Ordinal);
        return result;
    }

    private async Task<Dictionary<string, string>> CurrentValuesAsync(SecretOwner owner, HashSet<string>? names, CancellationToken token)
        => (await CurrentAsync(owner, names, token))
            .ToDictionary(secret => secret.Name, secret => protector.Unprotect(secret.ProtectedValue), StringComparer.Ordinal);

    private sealed record CurrentSecret(string Name, string ProtectedValue);

    /// <summary>The current, unrevoked, unexpired version of each secret of an owner, optionally by name.</summary>
    private async Task<List<CurrentSecret>> CurrentAsync(SecretOwner owner, HashSet<string>? names, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var definitions = Definitions(owner);
        if (names is not null) definitions = definitions.Where(secret => names.Contains(secret.Name));
        var current = await definitions.SelectMany(secret => secret.Versions
                .Where(version => version.Version == secret.CurrentVersion && version.RevokedAtUtc == null
                    && (version.ExpiresAtUtc == null || version.ExpiresAtUtc > now))
                .Select(version => new { secret.Name, version.ProtectedValue }))
            .ToListAsync(token);
        return current.Select(secret => new CurrentSecret(secret.Name, secret.ProtectedValue)).ToList();
    }

    private async Task<IReadOnlyList<ApplicationSecretDeclaration>> DeclarationsAsync(Guid appId, CancellationToken token)
    {
        var json = await db.WebApps.Where(app => app.Id == appId).Select(app => app.ApplicationContractJson).SingleOrDefaultAsync(token);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return ApplicationContractSerialization.Deserialize(json).Secrets ?? []; }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException)
        {
            // A stored contract that no longer validates declares nothing; app secrets still apply.
            return [];
        }
    }

    private IQueryable<SecretDefinition> Definitions(SecretOwner owner)
    {
        var (type, id) = (owner.OwnerType, owner.OwnerId);
        return db.SecretDefinitions.Where(secret => secret.OwnerType == type && secret.OwnerId == id && secret.DeletedAtUtc == null);
    }

    private async Task EnsureCanUpdateAppAsync(Guid appId, Guid userId, CancellationToken token)
    {
        if (!await authorization.CanUpdateAppAsync(appId, userId, token))
            throw new UnauthorizedAccessException("You do not have permission to manage this app's secrets.");
    }
}
