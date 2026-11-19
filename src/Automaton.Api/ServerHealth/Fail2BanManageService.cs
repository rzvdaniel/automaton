using System.Net;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.ServerCleanup;
using Automaton.Core.Models;

namespace Automaton.Api.ServerHealth;

/// <summary>
/// Unbans or allowlists one address through the Machine Management Runner that reports fail2ban, for an app with the approved
/// <c>security:manage</c> permission acting for a system administrator. The Runner runs fixed fail2ban commands; the app never
/// sends a command. Every request is audit-logged before it runs.
/// </summary>
public sealed class Fail2BanManageService(HostHealthReportService reports, IRunnerMaintenanceCommands commands, IAuditEventService audit)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public Task<Fail2BanActionResult> UnbanAsync(Guid appId, Guid viewerId, string? ip, CancellationToken token)
        => RunAsync(appId, viewerId, ip, "fail2ban-unban", "unban", token);

    public Task<Fail2BanActionResult> AllowlistAsync(Guid appId, Guid viewerId, string? ip, CancellationToken token)
        => RunAsync(appId, viewerId, ip, "fail2ban-allowlist", "allowlist", token);

    private async Task<Fail2BanActionResult> RunAsync(Guid appId, Guid viewerId, string? ip, string kind, string operation, CancellationToken token)
    {
        if (!IPAddress.TryParse(ip, out var address) || ip!.Contains('%') || ip.Length > 45 || ip.Any(char.IsWhiteSpace))
            throw new ArgumentException("Give one IPv4 or IPv6 address.");
        var canonical = address.ToString();
        var runnerId = await reports.LatestFail2BanRunnerAsync(token)
            ?? throw new InvalidOperationException("No machine has reported a running fail2ban.");
        await audit.WriteAsync(AuditActions.Requested, AuditEntityTypes.Runner, runnerId,
            $"fail2ban {operation} of {canonical} requested by an app for a system administrator",
            new { appId, viewerId, runnerId, ip = canonical, operation = $"fail2ban-{operation}" }, cancellationToken: token);
        try
        {
            var json = await commands.SendAsync(runnerId, viewerId, AppActionCommand.Create(kind, new Fail2BanAddressRequest(canonical)), Timeout, token);
            return json.Deserialize<Fail2BanActionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("The Runner answered with nothing.");
        }
        catch (RuntimeCommandFailedException exception) { throw new InvalidOperationException(exception.Message, exception); }
    }
}
