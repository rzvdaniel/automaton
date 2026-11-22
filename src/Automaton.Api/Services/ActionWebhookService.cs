using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Webhooks start an app's Action from outside Automaton. Owners manage them in Studio or the CLI; the caller posts to
/// <c>/api/hooks/{id}</c> with the secret, and anyone without it gets the same 404 as for an address that does not exist.
/// </summary>
public sealed class ActionWebhookService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    IActionScheduleTargetResolver targets,
    AppActionExecutionService executions,
    IAuditEventService audit)
{
    public const int MaxBodyBytes = 64 * 1024;
    public const int MaxWebhooksPerApp = 20;
    public const int CallsPerMinute = 30;
    private const int KeptCalls = 50;
    private const int ShownCalls = 10;
    private const string SecretPrefix = "whk_";

    public async Task<IReadOnlyList<ActionWebhookView>> ListAsync(Guid appId, Guid userId, string baseUrl, CancellationToken ct)
    {
        await EnsureCanUseAppAsync(appId, userId, ct);
        var hooks = await db.ActionWebhooks.AsNoTracking().Where(x => x.ApplicationId == appId).OrderBy(x => x.Name).ToListAsync(ct);
        var ids = hooks.Select(x => x.Id).ToList();
        var calls = await db.ActionWebhookCalls.AsNoTracking().Where(x => ids.Contains(x.WebhookId)).ToListAsync(ct);
        return hooks.Select(hook => ToView(hook, baseUrl, calls.Where(c => c.WebhookId == hook.Id), null)).ToList();
    }

    public async Task<ActionWebhookView> CreateAsync(Guid appId, CreateActionWebhookRequest request, Guid userId, string baseUrl, CancellationToken ct)
    {
        await EnsureCanUseAppAsync(appId, userId, ct);
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 160) throw new InvalidOperationException("Webhook name is required and cannot exceed 160 characters.");
        AppActionManifestParser.ValidateKey(request.ActionKey);
        await targets.ResolveAsync(appId, request.ActionKey, userId, ct);
        if (await db.ActionWebhooks.CountAsync(x => x.ApplicationId == appId, ct) >= MaxWebhooksPerApp)
            throw new InvalidOperationException($"An app can have at most {MaxWebhooksPerApp} webhooks.");
        var secret = NewSecret();
        var now = DateTime.UtcNow;
        var hook = new ActionWebhook
        {
            Id = Guid.NewGuid(), ApplicationId = appId, OwnerUserId = userId, ActionKey = request.ActionKey, Name = name,
            SecretHash = Hash(secret), Enabled = true, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.ActionWebhooks.Add(hook);
        await db.SaveChangesAsync(ct);
        await AuditManagedAsync(hook, AuditEventTypes.ActionWebhookCreated, "create", "Action webhook created", ct);
        return ToView(hook, baseUrl, [], secret);
    }

    public async Task<ActionWebhookView> SetEnabledAsync(Guid appId, Guid id, bool enabled, Guid userId, string baseUrl, CancellationToken ct)
    {
        var hook = await FindManagedAsync(appId, id, userId, ct);
        hook.Enabled = enabled;
        hook.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuditManagedAsync(hook, enabled ? AuditEventTypes.ActionWebhookEnabled : AuditEventTypes.ActionWebhookDisabled,
            enabled ? "enable" : "disable", $"Action webhook {(enabled ? "enabled" : "disabled")}", ct);
        return await ViewAsync(hook, baseUrl, null, ct);
    }

    /// <summary>Replaces the secret. The old one stops working at once; the new one is shown only in this answer.</summary>
    public async Task<ActionWebhookView> RotateSecretAsync(Guid appId, Guid id, Guid userId, string baseUrl, CancellationToken ct)
    {
        var hook = await FindManagedAsync(appId, id, userId, ct);
        var secret = NewSecret();
        hook.SecretHash = Hash(secret);
        hook.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuditManagedAsync(hook, AuditEventTypes.ActionWebhookSecretRotated, "rotate", "Action webhook secret rotated", ct);
        return await ViewAsync(hook, baseUrl, secret, ct);
    }

    public async Task DeleteAsync(Guid appId, Guid id, Guid userId, CancellationToken ct)
    {
        var hook = await FindManagedAsync(appId, id, userId, ct);
        db.ActionWebhookCalls.RemoveRange(await db.ActionWebhookCalls.Where(x => x.WebhookId == id).ToListAsync(ct));
        db.ActionWebhooks.Remove(hook);
        await db.SaveChangesAsync(ct);
        await AuditManagedAsync(hook, AuditEventTypes.ActionWebhookDeleted, "delete", "Action webhook deleted", ct);
    }

    /// <summary>Handles one call from outside. <paramref name="body"/> is the raw request body, already limited by the caller.</summary>
    public async Task<ActionWebhookCallResult> CallAsync(Guid id, string? presentedSecret, string body, CancellationToken ct)
    {
        var found = await db.ActionWebhooks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (found is null || !SecretMatches(found.SecretHash, presentedSecret)) return NotFound;
        var hook = found;

        var since = DateTime.UtcNow.AddMinutes(-1);
        if (await db.ActionWebhookCalls.CountAsync(x => x.WebhookId == id && x.CalledAtUtc >= since, ct) >= CallsPerMinute)
            return new(StatusCodes.Status429TooManyRequests, "Too many calls to this webhook. Try again in a minute.");

        var refused = await RefusalAsync(hook, ct);
        if (refused is not null) return refused;

        var (input, invalid) = await ReadInputAsync(hook, body, ct);
        if (invalid is not null) return invalid;

        if (await IsRunningAsync(hook, ct))
            return await RecordAsync(hook, ActionWebhookCallOutcomes.Skipped, StatusCodes.Status409Conflict, "The Action is already running, so this call was skipped.", null, ct);

        try
        {
            var execution = await executions.ExecuteWebhookAsync(hook.ApplicationId, hook.ActionKey, hook.OwnerUserId, input, hook.Name, ct);
            return await RecordAsync(hook, ActionWebhookCallOutcomes.Started, StatusCodes.Status202Accepted, "Started.", execution.Id, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or AppPausedException)
        {
            return await RecordAsync(hook, ActionWebhookCallOutcomes.Failed, StatusCodes.Status503ServiceUnavailable, "The Action could not be started right now.", null, ct, ex.Message);
        }
    }

    private async Task<ActionWebhookCallResult?> RefusalAsync(ActionWebhook hook, CancellationToken ct)
    {
        if (!hook.Enabled)
            return await RecordAsync(hook, ActionWebhookCallOutcomes.Disabled, StatusCodes.Status403Forbidden, "This webhook is disabled.", null, ct);
        if (await db.WebApps.AnyAsync(app => app.Id == hook.ApplicationId && app.IsPaused, ct))
            return await RecordAsync(hook, ActionWebhookCallOutcomes.AppPaused, StatusCodes.Status409Conflict, "The app is paused, so its Actions do not run. Resume the app first.", null, ct);
        if (!await authorization.CanExecuteAppAsync(hook.ApplicationId, hook.OwnerUserId, ct))
            return await RecordAsync(hook, ActionWebhookCallOutcomes.Rejected, StatusCodes.Status403Forbidden, "The webhook's owner can no longer run this app's Actions.", null, ct);
        return null;
    }

    private async Task<(JsonElement Input, ActionWebhookCallResult? Invalid)> ReadInputAsync(ActionWebhook hook, string body, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var input = document.RootElement.Clone();
            var manifest = await targets.ResolveAsync(hook.ApplicationId, hook.ActionKey, hook.OwnerUserId, ct);
            var schemaError = AppActionSchemaValidator.Validate(input, manifest.Spec.Input);
            if (schemaError == null) return (input, null);
            return (default, await RecordAsync(hook, ActionWebhookCallOutcomes.Rejected, StatusCodes.Status400BadRequest, $"Input is invalid: {schemaError}", null, ct));
        }
        catch (JsonException)
        {
            return (default, await RecordAsync(hook, ActionWebhookCallOutcomes.Rejected, StatusCodes.Status400BadRequest, "The request body must be JSON.", null, ct));
        }
        catch (KeyNotFoundException)
        {
            return (default, await RecordAsync(hook, ActionWebhookCallOutcomes.Failed, StatusCodes.Status404NotFound, "The Action is no longer in the app.", null, ct));
        }
    }

    private Task<bool> IsRunningAsync(ActionWebhook hook, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.AppModuleExecutions.AnyAsync(x => x.AppId == hook.ApplicationId && x.ModuleKey == hook.ActionKey
            && (x.Status == AppModuleExecutionStatuses.Created || x.Status == AppModuleExecutionStatuses.Dispatching || x.Status == AppModuleExecutionStatuses.Running)
            && (x.ExecutionDeadlineUtc == null || x.ExecutionDeadlineUtc > now), ct);
    }

    private static readonly ActionWebhookCallResult NotFound = new(StatusCodes.Status404NotFound, "Not found.");

    private async Task<ActionWebhookCallResult> RecordAsync(
        ActionWebhook hook, string outcome, int status, string message, Guid? executionId, CancellationToken ct, string? detail = null)
    {
        var now = DateTime.UtcNow;
        db.ChangeTracker.Clear();
        db.ActionWebhookCalls.Add(new ActionWebhookCall
        {
            Id = Guid.NewGuid(), WebhookId = hook.Id, CalledAtUtc = now, Outcome = outcome, ExecutionId = executionId,
            Detail = Limit(detail ?? message, 500)
        });
        var tracked = await db.ActionWebhooks.SingleOrDefaultAsync(x => x.Id == hook.Id, ct);
        if (tracked is not null) tracked.LastCalledAtUtc = now;
        await db.SaveChangesAsync(ct);
        var stale = await db.ActionWebhookCalls.Where(x => x.WebhookId == hook.Id)
            .OrderByDescending(x => x.CalledAtUtc).Skip(KeptCalls).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.ActionWebhookCalls.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
        await audit.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.ActionWebhookCalled, Action = "call", EntityType = AuditEntityTypes.ActionWebhook,
            EntityId = hook.Id, ActorUserId = Guid.Empty, Message = $"Action webhook called: {hook.Name} ({outcome})",
            Outcome = outcome == ActionWebhookCallOutcomes.Started ? "Success" : "Failure",
            Metadata = new { hook.ApplicationId, hook.ActionKey, outcome, executionId }
        }, ct);
        return new(status, message, executionId);
    }

    private async Task EnsureCanUseAppAsync(Guid appId, Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanExecuteAppAsync(appId, userId, ct))
            throw new UnauthorizedAccessException("You do not have permission to manage webhooks for this App.");
    }

    private async Task<ActionWebhook> FindManagedAsync(Guid appId, Guid id, Guid userId, CancellationToken ct)
    {
        await EnsureCanUseAppAsync(appId, userId, ct);
        var hook = await db.ActionWebhooks.SingleOrDefaultAsync(x => x.Id == id && x.ApplicationId == appId, ct)
            ?? throw new KeyNotFoundException("Webhook not found.");
        if (hook.OwnerUserId != userId) throw new UnauthorizedAccessException("Only the person who created this webhook can change it.");
        return hook;
    }

    private async Task<ActionWebhookView> ViewAsync(ActionWebhook hook, string baseUrl, string? secret, CancellationToken ct)
    {
        var calls = await db.ActionWebhookCalls.AsNoTracking().Where(x => x.WebhookId == hook.Id).ToListAsync(ct);
        return ToView(hook, baseUrl, calls, secret);
    }

    private static ActionWebhookView ToView(ActionWebhook hook, string baseUrl, IEnumerable<ActionWebhookCall> calls, string? secret)
        => new(hook.Id, hook.ApplicationId, hook.ActionKey, hook.Name, hook.Enabled, hook.CreatedAtUtc, hook.LastCalledAtUtc,
            $"{baseUrl.TrimEnd('/')}/api/hooks/{hook.Id}",
            calls.OrderByDescending(c => c.CalledAtUtc).Take(ShownCalls)
                .Select(c => new ActionWebhookCallView(c.CalledAtUtc, c.Outcome, c.ExecutionId, c.Detail)).ToList(),
            secret);

    private Task AuditManagedAsync(ActionWebhook hook, string eventType, string action, string message, CancellationToken ct)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            EventType = eventType, Action = action, EntityType = AuditEntityTypes.ActionWebhook, EntityId = hook.Id,
            Message = $"{message}: {hook.Name}", Metadata = new { hook.ApplicationId, hook.ActionKey, hook.Enabled }
        }, ct);

    internal static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    internal static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    /// <summary>Compares in constant time and does the same work when the webhook or the secret is missing.</summary>
    internal static bool SecretMatches(string? storedHash, string? presented)
    {
        var expected = Encoding.ASCII.GetBytes(storedHash ?? new string('0', 64));
        var actual = Encoding.ASCII.GetBytes(Hash(presented ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(expected, actual) && storedHash != null && !string.IsNullOrEmpty(presented);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
