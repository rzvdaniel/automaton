using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Moves a Runner to the server's configured Runner image, by command or from Studio and the CLI.</summary>
public sealed class RunnerUpgradeService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    IRunnerActionCommands runner,
    IOptions<HostedRunnerOptions> hostedRunnerOptions)
{
    private static readonly TimeSpan BusyWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan StatusWait = TimeSpan.FromSeconds(30);

    /// <summary>The host command for an offline or broken Runner.</summary>
    public async Task<RunnerUpgradeInstructions> PrepareAsync(Guid id, CancellationToken token)
    {
        var entity = await LoadAsync(id, token);
        var image = hostedRunnerOptions.Value.DockerImage;
        return new(id, image, IsCurrent(entity.ImageDigest, image), RunnerUpgradeCommand.Build(id, entity.InstallationId, image, entity.Name));
    }

    /// <summary>Starts the upgrade on an online, idle Runner. The image is always the server's configured one.</summary>
    public async Task<RunnerUpgradeStatus> StartAsync(Guid id, CancellationToken token)
    {
        var entity = await LoadAsync(id, token);
        var image = hostedRunnerOptions.Value.DockerImage;
        if (!RunnerPresenceEvaluator.IsOnline(entity.ConnectionId, entity.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException("The Runner is offline. Run the manual upgrade command on its server instead.");
        if (IsCurrent(entity.ImageDigest, image))
            throw new InvalidOperationException("The Runner already uses the current image.");
        var since = DateTime.UtcNow - BusyWindow;
        if (await IsBusyAsync(id, since, token))
            throw new InvalidOperationException("The Runner is running a deploy or an Action. Try again when it has finished.");
        var script = RunnerUpgradeCommand.BuildScript(id, entity.InstallationId, image, entity.Name);
        try
        {
            await runner.SendAsync<UpgradeRunnerStarted>(id, "upgrade-runner", new UpgradeRunnerRequest(id, script), token);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("Unknown App Action command", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This Runner is too old to upgrade itself. Run the manual upgrade command on its server once; later upgrades can be done in one click.");
        }
        return new(id, RunnerUpgradeStates.InProgress, "The upgrade started. The Runner restarts and reconnects within a few minutes.", entity.ImageDigest);
    }

    /// <summary>Reports how an upgrade that was started is going: in progress, reconnected or rolled back.</summary>
    public async Task<RunnerUpgradeStatus> StatusAsync(Guid id, CancellationToken token)
    {
        var entity = await LoadAsync(id, token);
        var image = hostedRunnerOptions.Value.DockerImage;
        if (IsCurrent(entity.ImageDigest, image)
            && RunnerPresenceEvaluator.IsOnline(entity.ConnectionId, entity.LastHeartbeatAtUtc, DateTime.UtcNow))
            return new(id, RunnerUpgradeStates.Reconnected, "The Runner reconnected on the current image.", entity.ImageDigest);
        if (!RunnerPresenceEvaluator.IsOnline(entity.ConnectionId, entity.LastHeartbeatAtUtc, DateTime.UtcNow))
            return new(id, RunnerUpgradeStates.InProgress, "Waiting for the Runner to reconnect.", entity.ImageDigest);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(StatusWait);
        RunnerUpgradeHelperStatus helper;
        try { helper = await runner.SendAsync<RunnerUpgradeHelperStatus>(id, "upgrade-status", new { }, limit.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new(id, RunnerUpgradeStates.InProgress, "Waiting for the Runner to answer.", entity.ImageDigest);
        }
        return helper.State switch
        {
            "running" => new(id, RunnerUpgradeStates.InProgress, "The upgrade is running.", entity.ImageDigest),
            "failed" => new(id, RunnerUpgradeStates.RolledBack,
                "The upgraded Runner did not connect, so the previous container was restored. " + helper.Log, entity.ImageDigest),
            // The helper finished and the Runner is back, but it has not reported the new image yet.
            "succeeded" => new(id, RunnerUpgradeStates.InProgress, "Waiting for the Runner to report its new image.", entity.ImageDigest),
            _ => new(id, RunnerUpgradeStates.Unknown, "No upgrade was started on this Runner.", entity.ImageDigest)
        };
    }

    /// <summary>True when the Runner reports the configured image (compared by digest when one is configured).</summary>
    internal static bool IsCurrent(string? reported, string configured)
    {
        var digest = configured.IndexOf('@', StringComparison.Ordinal);
        return digest < 0
            ? string.Equals(reported, configured, StringComparison.Ordinal)
            : reported is not null && reported.EndsWith(configured[digest..], StringComparison.Ordinal);
    }

    /// <summary>Queued Runner commands, deploys and Actions that have not finished on this Runner.</summary>
    private async Task<bool> IsBusyAsync(Guid runnerId, DateTime since, CancellationToken token)
    {
        if (await db.RunnerExecutionOperations.AnyAsync(o => o.RunnerId == runnerId && o.ResultJson == null && o.CreatedAtUtc > since, token))
            return true;
        return await db.DeploymentOperations.AnyAsync(operation => operation.CreatedAtUtc > since
            && (operation.Status == DeploymentOperationStatuses.Pending || operation.Status == DeploymentOperationStatuses.Running)
            && db.AppDeploymentTargets.Any(target => target.WebAppId == operation.WebAppId
                && (operation.TargetSlug == null ? target.IsDefault : target.Slug == operation.TargetSlug)
                && target.ExecutionRunnerId == runnerId), token);
    }

    private async Task<Runner> LoadAsync(Guid id, CancellationToken token)
    {
        // Upgrading replaces the Runner's software without changing who may use it, so the owner may
        // upgrade a Runner that projects share; deleting it still requires removing it from them first.
        if (!await authorization.CanUpdateRunnerAsync(id, token))
            throw new UnauthorizedAccessException($"You do not have permission to manage runner {id}");
        return await db.Runners.AsNoTracking().SingleAsync(entity => entity.Id == id, token);
    }
}
