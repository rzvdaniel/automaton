using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using static Automaton.Api.Hubs.AutomatonHub;

namespace Automaton.Api.Services;

/// <summary>
/// Compatibility proxy for the publishing surface while static publication is
/// migrated to the generic runtime endpoint. It does not execute application
/// code or deploy controller/backend artifacts.
/// </summary>
public sealed class RuntimeFileProxyService(
    IHubContext<AutomatonHub> automatonHub,
    TunnelRequestRegistry tunnelRegistry,
    IOptions<AppsTunnelConfig> tunnelOptions,
    ApiInstanceIdentityService apiInstanceIdentityService,
    IAppDeploymentTargetResolver deploymentTargetResolver,
    AppRunnerAssignmentPolicy runnerAssignmentPolicy)
{
    private readonly AppsTunnelConfig tunnelConfig = tunnelOptions.Value;

    public async Task<(byte[] Content, string ContentType)?> ReadAsync(
        Guid appId,
        Guid ownerId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveAsync(
            appId,
            targetSlug: null,
            ownerId,
            cancellationToken);
        var runner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            appId,
            ownerId,
            requireOnline: true,
            cancellationToken: cancellationToken);

        var body = JsonSerializer.Serialize(new
        {
            app_id = appId,
            deployment_target_id = target.Id,
            path = relativePath
        });
        var response = await SendTunnelRequestAsync(runner, "POST", "/api/runtime-file", body, cancellationToken);
        if (response.StatusCode == StatusCodes.Status404NotFound)
            return null;
        if (response.StatusCode < 200 || response.StatusCode >= 300)
            throw new InvalidOperationException(response.Error ?? $"Runner returned HTTP {response.StatusCode}.");

        var payload = JsonSerializer.Deserialize<RunnerRuntimeFileResponse>(
            response.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (string.IsNullOrWhiteSpace(payload?.ContentBase64))
            return null;

        return (
            Convert.FromBase64String(payload.ContentBase64),
            string.IsNullOrWhiteSpace(payload.ContentType) ? "application/octet-stream" : payload.ContentType);
    }

    private async Task<TunnelResponse> SendTunnelRequestAsync(
        Runner runner,
        string method,
        string path,
        string? body,
        CancellationToken cancellationToken)
    {
        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException($"Runner '{runner.Name}' is not connected");

        if (!string.IsNullOrWhiteSpace(runner.ApiInstanceId)
            && !string.Equals(runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Runner '{runner.Name}' belongs to another API instance.");
        }

        var (requestId, responseTask) = tunnelRegistry.Register(
            TimeSpan.FromSeconds(tunnelConfig.TimeoutSeconds), cancellationToken);
        await automatonHub.Clients.Client(runner.ConnectionId!)
            .SendAsync(AutomatonHubMethods.TunnelRequest, new TunnelRequest
            {
                RequestId = requestId,
                Method = method,
                Path = path,
                Body = body
            }, cancellationToken);

        return await responseTask;
    }

    private sealed class RunnerRuntimeFileResponse
    {
        public string? ContentBase64 { get; set; }
        public string? ContentType { get; set; }
    }
}
