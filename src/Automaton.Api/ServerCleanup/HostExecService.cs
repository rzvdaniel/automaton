using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.ServerCleanup;

/// <summary>Why a host command was refused (shown to the caller; maps to 403).</summary>
public sealed class HostExecRefusedException(string message) : Exception(message);

public sealed record HostExecRunnerDto(Guid RunnerId, string Name, bool Approved, bool Suspended, string? ApprovedFor, string CurrentCode);

public sealed record HostExecSettingsDto(bool Enabled);

/// <summary>Sends the <c>host-exec</c> command to a Runner; the stored copy of the request and answer is emptied afterwards.</summary>
public interface IHostExecCommands
{
    Task<HostExecResult> SendAsync(Guid runnerId, Guid actor, HostExecRequest request, TimeSpan timeout, CancellationToken token);
}

public sealed class HostExecCommands(RunnerExecutionOperationService operations) : IHostExecCommands
{
    public async Task<HostExecResult> SendAsync(Guid runnerId, Guid actor, HostExecRequest request, TimeSpan timeout, CancellationToken token)
    {
        var result = await operations.QueueAndWaitScrubbedAsync(runnerId, actor,
            new RunnerExecutionRequest(AppActionCommand.Create("host-exec", request)), timeout, token);
        return (result.ActionResult ?? throw new InvalidDataException("The Runner answered with nothing."))
            .Deserialize<HostExecResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The Runner answered with nothing.");
    }
}

/// <summary>
/// <c>runner:host-exec</c>: an app owned by a system administrator runs a script as root on one chosen machine. Every guard is checked on every
/// call: the server switch (<c>HostExec.Enabled</c>), the owner, the per-machine approval and the app's code the approval was given for.
/// </summary>
public sealed class HostExecService(ApplicationDbContext db, AppPermissionService permissions, ResourceAuthorizationService authorization,
    IAuditEventService audit, IHostExecCommands commands)
{
    public const string SettingKey = "HostExec.Enabled";
    public const string Permission = AppPermissionApproval.RunnerHostExec;
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(3);

    public async Task<bool> IsEnabledAsync(CancellationToken token)
        => await db.Settings.AsNoTracking().Where(item => item.Key == SettingKey).Select(item => item.Value).SingleOrDefaultAsync(token) == "true";

    /// <summary>Turns host access on or off for the whole server. Turning it off revokes every approval.</summary>
    public async Task<HostExecSettingsDto> SetEnabledAsync(bool enabled, Guid? actor, CancellationToken token)
    {
        var row = await db.Settings.SingleOrDefaultAsync(item => item.Key == SettingKey, token);
        if (row is null) db.Settings.Add(row = new Setting { Key = SettingKey, Description = "Apps owned by a system administrator may run commands as root on approved machines." });
        var wasEnabled = row.Value == "true";
        row.Value = enabled ? "true" : "false";
        row.UpdatedAt = DateTime.UtcNow;
        var revoked = 0;
        if (!enabled)
        {
            var rows = await db.AppRunnerPermissions.Where(item => item.Permission == Permission).ToListAsync(token);
            revoked = rows.Count;
            db.AppRunnerPermissions.RemoveRange(rows);
        }
        await db.SaveChangesAsync(token);
        if (wasEnabled != enabled || revoked > 0)
            await audit.WriteAsync(new AuditEventWriteRequest
            {
                Action = enabled ? AuditActions.Enabled : AuditActions.Disabled, EntityType = AuditEntityTypes.ServerInstallation,
                Message = enabled ? "Host access for apps (runner:host-exec) was turned on"
                    : $"Host access for apps (runner:host-exec) was turned off; {revoked} approval(s) were revoked",
                ActorUserId = actor, Metadata = new { setting = SettingKey, enabled, revoked }
            }, token);
        return new(enabled);
    }

    /// <summary>Every machine-management Runner with whether this app may run host commands on it.</summary>
    public async Task<IReadOnlyList<HostExecRunnerDto>> ListAsync(Guid appId, CancellationToken token)
    {
        var app = await LoadAppAsync(appId, token);
        var code = await CurrentCodeAsync(app, token);
        var rows = await db.AppRunnerPermissions.AsNoTracking().Where(item => item.AppId == appId && item.Permission == Permission).ToListAsync(token);
        var runners = await HostRunners().OrderBy(item => item.Name).Select(item => new { item.Id, item.Name }).ToListAsync(token);
        return runners.Select(runner =>
        {
            var row = rows.FirstOrDefault(item => item.RunnerId == runner.Id);
            return new HostExecRunnerDto(runner.Id, runner.Name, row is not null, row is not null && row.CodeFingerprint != code.Fingerprint,
                row?.ApprovedFor, code.Description);
        }).ToList();
    }

    /// <summary>A system administrator approves (again) or revokes host commands for one app on one machine.</summary>
    public async Task<IReadOnlyList<HostExecRunnerDto>> SetAsync(Guid appId, Guid runnerId, bool approved, Guid? actor, CancellationToken token)
    {
        var app = await LoadAppAsync(appId, token);
        var runner = await HostRunners().Where(item => item.Id == runnerId).Select(item => new { item.Id, item.Name }).SingleOrDefaultAsync(token)
            ?? throw new KeyNotFoundException("That machine was not found.");
        var row = approved ? await ApproveAsync(app, runnerId, token) : await RevokeAsync(appId, runnerId, token);
        if (row is null && !approved) return await ListAsync(appId, token);
        await audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = approved ? AuditActions.Approved : AuditActions.Revoked, EntityType = AuditEntityTypes.AppPermission, EntityId = appId, ActorUserId = actor,
            Message = $"App permission {Permission} (any command as root) was {(approved ? "approved" : "revoked")} for Runner {runner.Name}",
            Metadata = new { permission = Permission, appId, runnerId, runner = runner.Name, approvedFor = row?.ApprovedFor }
        }, token);
        return await ListAsync(appId, token);
    }

    private async Task<AppRunnerPermission> ApproveAsync(AppRow app, Guid runnerId, CancellationToken token)
    {
        if (!await IsEnabledAsync(token)) throw new InvalidOperationException("Host access is turned off for this server. Turn it on in the server settings first.");
        await EnsureAdministratorOwnedAsync(app, token);
        var code = await CurrentCodeAsync(app, token);
        var row = await db.AppRunnerPermissions.SingleOrDefaultAsync(item => item.AppId == app.Id && item.RunnerId == runnerId && item.Permission == Permission, token);
        if (row is null) db.AppRunnerPermissions.Add(row = new AppRunnerPermission { Id = Guid.NewGuid(), AppId = app.Id, RunnerId = runnerId, Permission = Permission });
        row.ApprovedAtUtc = DateTime.UtcNow;
        row.CodeFingerprint = code.Fingerprint;
        row.ApprovedFor = code.Description;
        var status = (await permissions.GetStatusAsync(app.Id, token)).FirstOrDefault(item => item.Permission == Permission)
            ?? throw new InvalidOperationException("The app has not requested runner:host-exec.");
        await db.SaveChangesAsync(token);
        if (!status.Approved) await permissions.SetApprovalAsync(app.Id, Permission, true, token);
        return row;
    }

    private async Task<AppRunnerPermission?> RevokeAsync(Guid appId, Guid runnerId, CancellationToken token)
    {
        var row = await db.AppRunnerPermissions.SingleOrDefaultAsync(item => item.AppId == appId && item.RunnerId == runnerId && item.Permission == Permission, token);
        if (row is null) return null;
        db.AppRunnerPermissions.Remove(row);
        await db.SaveChangesAsync(token);
        if (!await db.AppRunnerPermissions.AnyAsync(item => item.AppId == appId && item.Permission == Permission, token))
            await permissions.SetApprovalAsync(appId, Permission, false, token);
        return row;
    }

    /// <summary>Runs a script as root on one machine, after every guard; the run is audit-logged when it starts and when it ends.</summary>
    public async Task<HostExecResult> RunAsync(Guid appId, Guid viewerId, Guid runnerId, HostExecRequest request, CancellationToken token)
    {
        if (!await IsEnabledAsync(token)) throw new HostExecRefusedException("Host access is turned off for this server.");
        var app = await LoadAppAsync(appId, token);
        try { await EnsureAdministratorOwnedAsync(app, token); }
        catch (InvalidOperationException exception)
        {
            await RevokeAllAsync(app, token);
            throw new HostExecRefusedException(exception.Message);
        }
        var runner = await HostRunners().Where(item => item.Id == runnerId).Select(item => new { item.Name, item.ApiCompatibilityVersion }).SingleOrDefaultAsync(token)
            ?? throw new KeyNotFoundException("That machine was not found.");
        var row = await db.AppRunnerPermissions.AsNoTracking().SingleOrDefaultAsync(item => item.AppId == appId && item.RunnerId == runnerId && item.Permission == Permission, token)
            ?? throw new HostExecRefusedException($"This app is not approved to run host commands on {runner.Name}.");
        var code = await CurrentCodeAsync(app, token);
        if (OwnersOf(row.CodeFingerprint) != OwnersOf(code.Fingerprint))
        {
            // The app changed owner: the approval is gone, not just suspended.
            db.AppRunnerPermissions.Remove(await db.AppRunnerPermissions.SingleAsync(item => item.Id == row.Id, token));
            await db.SaveChangesAsync(token);
            await audit.WriteAsync(AuditActions.Revoked, AuditEntityTypes.AppPermission, appId,
                $"App permission {Permission} was revoked for Runner {runner.Name} because the app changed owner",
                new { permission = Permission, appId, runnerId, runner = runner.Name }, cancellationToken: token);
            throw new HostExecRefusedException("The app changed owner, so its approval was revoked.");
        }
        if (row.CodeFingerprint != code.Fingerprint)
            throw new HostExecRefusedException($"The app changed since it was approved ({row.ApprovedFor} -> {code.Description}). A system administrator must approve it again on {runner.Name}.");
        if (!RunnerProtocolCompatibilityPolicy.SupportsHostExec(runner.ApiCompatibilityVersion))
            throw new InvalidOperationException($"Runner {runner.Name} is too old to run host commands. Upgrade the Runner first.");
        var timeoutSeconds = request.TimeoutSeconds == 0 ? HostExecRequest.DefaultTimeoutSeconds : request.TimeoutSeconds;
        var checkedRequest = request with { TimeoutSeconds = timeoutSeconds };
        Validate(checkedRequest);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Script))).ToLowerInvariant();
        var started = DateTime.UtcNow;
        await WriteRunAsync(AuditActions.Started, app.Name, runner.Name, viewerId, appId, runnerId, hash, started, null, null,
            $"{app.Name} is running a host command on {runner.Name}", "Success", token);
        try
        {
            var result = await commands.SendAsync(runnerId, viewerId, checkedRequest, TimeSpan.FromSeconds(timeoutSeconds) + Margin, token);
            var ok = !result.TimedOut && result.ExitCode == 0;
            await WriteRunAsync(ok ? AuditActions.Succeeded : AuditActions.Failed, app.Name, runner.Name, viewerId, appId, runnerId, hash, started, DateTime.UtcNow, result.ExitCode,
                result.TimedOut ? $"{app.Name} ran a host command on {runner.Name}, stopped at the timeout" : $"{app.Name} ran a host command on {runner.Name}, exit {result.ExitCode}",
                ok ? "Success" : "Failure", token);
            return result;
        }
        catch (RuntimeCommandFailedException exception)
        {
            await WriteRunAsync(AuditActions.Failed, app.Name, runner.Name, viewerId, appId, runnerId, hash, started, DateTime.UtcNow, null,
                $"{app.Name} could not run a host command on {runner.Name}", "Failure", token);
            throw new InvalidOperationException(exception.Message, exception);
        }
    }

    /// <summary>An app that is no longer administrator-owned keeps no approval.</summary>
    private async Task RevokeAllAsync(AppRow app, CancellationToken token)
    {
        var stale = await db.AppRunnerPermissions.Where(item => item.AppId == app.Id && item.Permission == Permission).ToListAsync(token);
        if (stale.Count == 0) return;
        db.AppRunnerPermissions.RemoveRange(stale);
        await db.SaveChangesAsync(token);
        await audit.WriteAsync(AuditActions.Revoked, AuditEntityTypes.AppPermission, app.Id,
            $"App permission {Permission} was revoked for {app.Name} because it is no longer owned by a system administrator",
            new { permission = Permission, appId = app.Id }, cancellationToken: token);
    }

    private static string OwnersOf(string? fingerprint) => fingerprint?[(fingerprint.LastIndexOf('#') + 1)..] ?? "";

    internal static void Validate(HostExecRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Script)) throw new ArgumentException("The script is empty.");
        if (request.Script.Length > HostExecRequest.MaximumScriptLength || request.Script.Contains('\0'))
            throw new ArgumentException($"The script can be at most {HostExecRequest.MaximumScriptLength} characters.");
        if ((request.Stdin?.Length ?? 0) > HostExecRequest.MaximumStdinLength) throw new ArgumentException($"The input can be at most {HostExecRequest.MaximumStdinLength} characters.");
        if (request.TimeoutSeconds is < 1 or > HostExecRequest.MaximumTimeoutSeconds)
            throw new ArgumentException($"The timeout must be from 1 to {HostExecRequest.MaximumTimeoutSeconds} seconds.");
    }

    // Only the app, machine, person, times, exit code and a hash of the script are recorded: never the script, its input or its output.
    private Task WriteRunAsync(string action, string appName, string runnerName, Guid viewerId, Guid appId, Guid runnerId, string scriptSha256,
        DateTime started, DateTime? ended, int? exitCode, string message, string outcome, CancellationToken token)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = action, EntityType = AuditEntityTypes.Runner, EntityId = runnerId, ActorUserId = viewerId, Outcome = outcome,
            Message = message + " (started by " + StartedBy(viewerId) + ")",
            Metadata = new { operation = "host-exec", appId, app = appName, runnerId, runner = runnerName, viewerId, startedUtc = started, endedUtc = ended, exitCode, scriptSha256 }
        }, token);

    private string StartedBy(Guid viewerId)
        => db.Users.AsNoTracking().Where(user => user.Id == viewerId).Select(user => user.DisplayName ?? user.Name ?? user.UserName).FirstOrDefault() ?? viewerId.ToString();

    private sealed record AppRow(Guid Id, string Name, int Version, string? InstalledFromCommit, string? DeployedFromCommit, Guid? ProjectId, DateTimeOffset? Stamp);

    private async Task<AppRow> LoadAppAsync(Guid appId, CancellationToken token)
        => await db.WebApps.AsNoTracking().Where(app => app.Id == appId)
            .Select(app => new AppRow(app.Id, app.Name, app.Version, app.InstalledFromCommit, app.DeployedFromCommit, app.ProjectId,
                app.DeploymentTargets.Max(target => target.LastSuccessfulDeploymentAt)))
            .SingleOrDefaultAsync(token) ?? throw new KeyNotFoundException("App not found.");

    private async Task<Guid[]> OwnerIdsAsync(AppRow app, CancellationToken token)
    {
        if (app.ProjectId is { } projectId)
        {
            var owner = await db.Projects.AsNoTracking().Where(project => project.Id == projectId).Select(project => (Guid?)project.OwnerId).SingleOrDefaultAsync(token);
            if (owner is { } id) return [id];
        }
        return (await db.WebAppUsers.AsNoTracking().Where(link => link.WebAppId == app.Id).Select(link => link.UserId).Distinct().ToListAsync(token))
            .OrderBy(id => id).ToArray();
    }

    private async Task EnsureAdministratorOwnedAsync(AppRow app, CancellationToken token)
    {
        var owners = await OwnerIdsAsync(app, token);
        if (owners.Length == 0) throw new InvalidOperationException("This app has no owner, so it cannot run host commands.");
        foreach (var owner in owners)
            if (!await authorization.IsSystemAdministratorAsync(owner, token))
                throw new InvalidOperationException("Only apps owned by a system administrator can run host commands.");
    }

    /// <summary>The owner and the code the approval is tied to: a new version, commit or deployment, or another owner, changes it.</summary>
    private async Task<(string Fingerprint, string Description)> CurrentCodeAsync(AppRow app, CancellationToken token)
    {
        var owners = string.Join(',', await OwnerIdsAsync(app, token));
        var commit = app.DeployedFromCommit ?? app.InstalledFromCommit;
        var short_ = string.IsNullOrEmpty(commit) ? "no commit" : "commit " + commit[..Math.Min(commit.Length, 8)];
        var deployed = app.Stamp?.UtcDateTime.ToString("u") ?? "never deployed";
        return ($"v{app.Version}|{commit}|{app.Stamp?.UtcTicks}#{owners}", $"version {app.Version}, {short_}, deployed {deployed}");
    }

    private IQueryable<Runner> HostRunners() => db.Runners.AsNoTracking().Where(item =>
        item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.DeletionRequestedAtUtc == null
        && item.InstallationId != null && item.InstallationId != "");
}
