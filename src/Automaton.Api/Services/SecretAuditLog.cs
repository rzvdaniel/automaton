using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>One secret handed to an Action run or a runtime start: its name, where it came from and which version.</summary>
public sealed record SecretDelivery(string Name, string Scope, int? Version, string? From = null);

/// <summary>One audit entry about a vault, as shown in a secrets History list. Never carries a value.</summary>
public sealed record SecretAuditEntry(
    DateTime CreatedAtUtc, string EventType, string Message, Guid? UserId, string? UserName,
    string? Name, int? Version, Guid? AppId, Guid? RunnerId, IReadOnlyList<SecretDelivery> Delivered);

/// <summary>
/// Records secret changes and deliveries in the platform audit log (names, scopes and versions, never values)
/// and reads them back per vault. Entries are kept for <see cref="RetentionDays"/> days.
/// </summary>
public sealed class SecretAuditLog(IAuditEventService audit, ApplicationDbContext db)
{
    public const int RetentionDays = 180;
    public const int HistoryLimit = 100;

    private static readonly string[] EntityTypes =
        [AuditEntityTypes.AppSecretVault, AuditEntityTypes.ProjectSecretVault, AuditEntityTypes.UserSecretVault];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task ChangedAsync(SecretOwner owner, Guid appId, Guid actorUserId, string name, int? version, CancellationToken token)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = version is null ? AuditActions.Deleted : AuditActions.Changed,
            EntityType = EntityType(owner.OwnerType),
            EntityId = owner.OwnerId,
            EventType = version is null ? AuditEventTypes.SecretDeleted : AuditEventTypes.SecretSet,
            ActorUserId = actorUserId,
            Message = version is null
                ? $"Secret {name} was deleted from the {Scope(owner.OwnerType)} vault"
                : $"Secret {name} was set to version {version} in the {Scope(owner.OwnerType)} vault",
            Metadata = new AuditMetadata(name, version, appId, null, null)
        }, token);

    /// <summary>One entry per delivery listing every secret delivered, recorded on the app's vault.</summary>
    public Task DeliveredAsync(Guid appId, IReadOnlyList<SecretDelivery> delivered, Guid? actorUserId, Guid? runnerId,
        CancellationToken token)
    {
        if (delivered.Count == 0) return Task.CompletedTask;
        return audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Delivered,
            EntityType = AuditEntityTypes.AppSecretVault,
            EntityId = appId,
            EventType = AuditEventTypes.SecretsDelivered,
            ActorUserId = actorUserId,
            Message = runnerId is null
                ? $"{delivered.Count} secret(s) delivered to an Action run"
                : $"{delivered.Count} secret(s) delivered to a runtime start",
            Metadata = new AuditMetadata(null, null, appId, runnerId, delivered)
        }, token);
    }

    public async Task<IReadOnlyList<SecretAuditEntry>> HistoryAsync(SecretOwner owner, CancellationToken token)
    {
        var entityType = EntityType(owner.OwnerType);
        var rows = await db.AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityType == entityType && entry.EntityId == owner.OwnerId)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(HistoryLimit)
            .ToListAsync(token);
        return rows.Select(row =>
        {
            var metadata = Read(row.MetadataJson);
            return new SecretAuditEntry(row.CreatedAtUtc, row.EventType, row.Message, row.UserId, row.UserName,
                metadata?.Name, metadata?.Version, metadata?.AppId, metadata?.RunnerId, metadata?.Delivered ?? []);
        }).ToList();
    }

    /// <summary>Removes secret audit entries older than the retention window.</summary>
    public static async Task<int> RemoveExpiredAsync(ApplicationDbContext db, DateTime now, CancellationToken token)
    {
        var cutoff = now.AddDays(-RetentionDays);
        var expired = db.AuditEvents.Where(entry => EntityTypes.Contains(entry.EntityType) && entry.CreatedAtUtc < cutoff);
        if (db.Database.IsRelational()) return await expired.ExecuteDeleteAsync(token);
        var rows = await expired.ToListAsync(token);
        db.AuditEvents.RemoveRange(rows);
        await db.SaveChangesAsync(token);
        return rows.Count;
    }

    private static AuditMetadata? Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<AuditMetadata>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static string EntityType(string ownerType) => ownerType switch
    {
        SecretOwnerTypes.App => AuditEntityTypes.AppSecretVault,
        SecretOwnerTypes.Project => AuditEntityTypes.ProjectSecretVault,
        SecretOwnerTypes.User => AuditEntityTypes.UserSecretVault,
        _ => throw new ArgumentOutOfRangeException(nameof(ownerType), ownerType, "Unknown secret owner type.")
    };

    private static string Scope(string ownerType) => ownerType switch
    {
        SecretOwnerTypes.Project => AppSecretScopes.Project,
        SecretOwnerTypes.User => AppSecretScopes.User,
        _ => AppSecretScopes.App
    };

    // Property names avoid the audit sanitizer's sensitive words (secret, key, token...), which it would redact.
    private sealed record AuditMetadata(string? Name, int? Version, Guid? AppId, Guid? RunnerId, IReadOnlyList<SecretDelivery>? Delivered);
}
