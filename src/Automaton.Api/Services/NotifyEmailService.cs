using System.Net.Mail;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.Services;

public sealed record NotifyEmailRequest(IReadOnlyList<string>? To, string? Subject, string? Text);
public sealed record NotifyEmailResult(int Sent);

public enum NotifyEmailFailure { Invalid, NotAllowed, NoRecipients, RateLimited, SmtpNotConfigured }

public sealed class NotifyEmailException(NotifyEmailFailure failure, string message, int? retryAfterSeconds = null) : Exception(message)
{
    public NotifyEmailFailure Failure { get; } = failure;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Lets an app with the approved <c>notify:email</c> permission email its owners, the server's administrators, the
/// members of its project and addresses on the administrators' allowed list, through the server's SMTP. The app never
/// receives an address it did not type.
/// </summary>
public sealed class NotifyEmailService(ApplicationDbContext db, SmtpSettingsService smtp, IMemoryCache memory, IAuditEventService audit)
{
    public const string AllowedSettingKey = "Notify.AllowedRecipients";
    public const string Owner = "owner", Administrators = "administrators", Project = "project";
    public const int MaxSendsPerHour = 10;
    public const int MaxRecipientsPerSend = 25;
    public const int MaxSubjectLength = 150;
    public const int MaxTextLength = 10_000;
    private static readonly object CounterLock = new();
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public async Task<NotifyEmailResult> SendAsync(Guid appId, NotifyEmailRequest request, CancellationToken token)
    {
        var app = await db.WebApps.AsNoTracking().Where(item => item.Id == appId)
            .Select(item => new { item.Name, item.ProjectId }).SingleOrDefaultAsync(token)
            ?? throw new KeyNotFoundException("App not found.");
        var (targets, subject, text) = Validate(request);
        Reserve(appId); // every valid-looking attempt counts, refused ones too, so a loop cannot flood the audit log
        var addresses = await ResolveAsync(appId, app.Name, app.ProjectId, targets, subject, token);
        var config = (await smtp.GetEffectiveAsync(token)).Config;
        if (!SmtpSettingsService.IsConfigured(config))
            throw new NotifyEmailException(NotifyEmailFailure.SmtpNotConfigured, new SmtpNotConfiguredException().Message);

        var prefixed = $"[{app.Name.ReplaceLineEndings(" ").Trim()}] {subject}";
        try { await smtp.SendPlainAsync(addresses.ToList(), prefixed, text, token); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await AuditAsync(appId, app.Name, AuditEventTypes.AppEmailRefused, "Failure", subject, targets.Count, 0, "the SMTP server did not accept the message");
            throw;
        }
        // The mail is out; a failing audit write must not tell the app it failed (it would retry and duplicate).
        try { await AuditAsync(appId, app.Name, AuditEventTypes.AppEmailSent, "Success", subject, targets.Count, addresses.Count, null); }
        catch (Exception) when (!token.IsCancellationRequested) { }
        return new(addresses.Count);
    }

    private static (List<string> Targets, string Subject, string Text) Validate(NotifyEmailRequest request)
    {
        var targets = (request.To ?? []).Select(item => item?.Trim() ?? "").Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var subject = (request.Subject ?? "").ReplaceLineEndings(" ").Trim();
        var text = request.Text ?? "";
        if (targets.Count == 0 || targets.Count > MaxRecipientsPerSend || subject.Length is 0 or > MaxSubjectLength
            || text.Trim().Length == 0 || text.Length > MaxTextLength)
            throw new NotifyEmailException(NotifyEmailFailure.Invalid,
                $"Send 1 to {MaxRecipientsPerSend} recipients, a subject of up to {MaxSubjectLength} characters and a text of up to {MaxTextLength} characters.");
        return (targets, subject, text);
    }

    private async Task<HashSet<string>> ResolveAsync(Guid appId, string appName, Guid? projectId, List<string> targets, string subject, CancellationToken token)
    {
        var allowed = await AllowedAsync(token);
        if (targets.Any(item => !IsGroup(item) && !allowed.Contains(item, StringComparer.OrdinalIgnoreCase)))
        {
            await AuditAsync(appId, appName, AuditEventTypes.AppEmailRefused, "Denied", subject, targets.Count, 0, "address not on the allowed list");
            throw new NotifyEmailException(NotifyEmailFailure.NotAllowed,
                "An address is not on the server's allowed list. Ask an administrator to add it, or use owner, administrators or project.");
        }
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (!IsGroup(target)) { addresses.Add(target); continue; }
            foreach (var address in await GroupAddressesAsync(appId, projectId, target.ToLowerInvariant(), token)) addresses.Add(address);
        }
        if (addresses.Count == 0)
            throw new NotifyEmailException(NotifyEmailFailure.NoRecipients, "None of the recipients has an email address (an app outside a project has no project members).");
        if (addresses.Count > MaxRecipientsPerSend)
            throw new NotifyEmailException(NotifyEmailFailure.Invalid, $"At most {MaxRecipientsPerSend} recipients per message.");
        return addresses;
    }

    public async Task<IReadOnlyList<string>> AllowedAsync(CancellationToken token)
    {
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == AllowedSettingKey, token);
        if (row is null) return [];
        try { return JsonSerializer.Deserialize<string[]>(row.Value) ?? []; }
        catch (JsonException) { return []; }
    }

    public async Task SetAllowedAsync(IReadOnlyList<string>? addresses, CancellationToken token)
    {
        var clean = (addresses ?? []).Select(item => item?.Trim() ?? "").Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (clean.Count > 100 || clean.Any(item => !MailAddress.TryCreate(item, out var parsed) || parsed.Address != item))
            throw new ArgumentException("Enter up to 100 valid email addresses.");
        var value = JsonSerializer.Serialize(clean);
        var row = await db.Settings.SingleOrDefaultAsync(item => item.Key == AllowedSettingKey, token);
        if (row is null)
            db.Settings.Add(new Setting { Key = AllowedSettingKey, Value = value, Description = "Addresses apps with notify:email may email." });
        else { row.Value = value; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(token);
        await audit.WriteAsync(AuditActions.Updated, AuditEntityTypes.ServerInstallation, null,
            $"The allowed list for app email now has {clean.Count} address(es)", new { count = clean.Count }, cancellationToken: token);
    }

    private static bool IsGroup(string target)
        => target.Equals(Owner, StringComparison.OrdinalIgnoreCase) || target.Equals(Administrators, StringComparison.OrdinalIgnoreCase)
            || target.Equals(Project, StringComparison.OrdinalIgnoreCase);

    private async Task<List<string>> GroupAddressesAsync(Guid appId, Guid? webAppProjectId, string group, CancellationToken token)
    {
        IQueryable<Guid> userIds;
        switch (group)
        {
            case Owner:
                userIds = db.WebAppUsers.Where(item => item.WebAppId == appId).Select(item => item.UserId);
                break;
            case Administrators:
                userIds = db.UserRoles.Where(item => db.Roles.Any(role => role.Id == item.RoleId && role.Name == UserRole.SystemAdministrator))
                    .Select(item => item.UserId);
                break;
            default:
                var projects = db.ProjectApps.Where(item => item.WebAppId == appId).Select(item => item.ProjectId);
                userIds = db.ProjectMembers.Where(item => item.IsActive && (projects.Contains(item.ProjectId) || item.ProjectId == webAppProjectId))
                    .Select(item => item.UserId);
                break;
        }
        return await db.Users.AsNoTracking().Where(user => !user.IsDeleted && user.Email != null && userIds.Contains(user.Id))
            .Select(user => user.Email!).ToListAsync(token);
    }

    private void Reserve(Guid appId)
    {
        lock (CounterLock)
        {
            var key = $"notify-email:{appId}";
            var counter = memory.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Window;
                return new SendCounter();
            })!;
            if (++counter.Count > MaxSendsPerHour)
                throw new NotifyEmailException(NotifyEmailFailure.RateLimited,
                    $"An app can send at most {MaxSendsPerHour} emails per hour.", (int)Window.TotalSeconds);
        }
    }

    private Task AuditAsync(Guid appId, string appName, string eventType, string outcome, string subject, int requested, int recipients, string? reason)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = eventType == AuditEventTypes.AppEmailSent ? AuditActions.Delivered : AuditActions.Failed,
            EntityType = AuditEntityTypes.App, EntityId = appId, EventType = eventType, Outcome = outcome,
            Message = eventType == AuditEventTypes.AppEmailSent
                ? $"App {appName} emailed {recipients} recipient(s)" : $"App {appName} email was not sent: {reason}",
            Metadata = new { appId, subject, targets = requested, recipients, reason }
        }, CancellationToken.None);

    private sealed class SendCounter { public int Count; }
}
