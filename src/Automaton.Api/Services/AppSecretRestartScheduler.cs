using System.Collections.Concurrent;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

/// <summary>Restarts an app's running copies after its secrets changed, so they start with the new environment.</summary>
public interface IAppSecretRestartScheduler
{
    /// <summary>Notes that the secret <paramref name="name"/> of the app changed; changes within a few seconds share one restart.</summary>
    void SecretChanged(Guid appId, Guid userId, string name);
}

/// <summary>
/// Collects the secret names changed per app for a short window, then queues one restart and records one Activity
/// entry naming them (never values). A copy that is not running gets the secrets at its next start anyway.
/// </summary>
public sealed class AppSecretRestartScheduler(IServiceScopeFactory scopes, ILogger<AppSecretRestartScheduler> logger)
    : IAppSecretRestartScheduler
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, Pending> pending = new();

    public void SecretChanged(Guid appId, Guid userId, string name)
    {
        while (true)
        {
            var batch = pending.GetOrAdd(appId, _ => new Pending(userId));
            lock (batch)
            {
                if (batch.Closed) continue;
                batch.Names.Add(name);
                if (batch.Started) return;
                batch.Started = true;
            }
            _ = Task.Run(() => FlushAsync(appId, batch));
            return;
        }
    }

    private async Task FlushAsync(Guid appId, Pending batch)
    {
        await Task.Delay(Window);
        string[] names;
        lock (batch)
        {
            batch.Closed = true;
            pending.TryRemove(appId, out _);
            names = batch.Names.Order(StringComparer.Ordinal).ToArray();
        }
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<UserContextService>().ImpersonateUser(batch.UserId);
            var restarted = await scope.ServiceProvider.GetRequiredService<IAppRuntimeRestarter>()
                .RestartRunningAsync(appId, CancellationToken.None);
            if (restarted == 0) return;
            var label = names.Length == 1 ? $"secret {names[0]}" : $"secrets {string.Join(", ", names)}";
            await scope.ServiceProvider.GetRequiredService<IAuditEventService>().WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Changed,
                EntityType = AuditEntityTypes.App,
                EntityId = appId,
                EventType = AuditEventTypes.AppRestartedForSecrets,
                Message = $"Restarted to apply {label}",
                Outcome = AuditOutcomes.Success,
                ActorUserId = batch.UserId,
                Metadata = new { SecretNames = names }
            });
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not restart app {AppId} after a secret change.", appId);
        }
    }

    private sealed class Pending(Guid userId)
    {
        public Guid UserId { get; } = userId;
        public HashSet<string> Names { get; } = [];
        public bool Started { get; set; }
        public bool Closed { get; set; }
    }
}
