using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Compatibility proxy for the publishing surface while static publication is
/// migrated to the generic runtime endpoint. It does not execute application
/// code or deploy controller/backend artifacts.
/// </summary>
public sealed class RuntimeFileProxyService(
    RunnerTunnelRegistry tunnels,
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
        using var response = await SendTunnelRequestAsync(runner, "/api/runtime-file", body, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Runner returned HTTP {(int)response.StatusCode}.");

        var payload = JsonSerializer.Deserialize<RunnerRuntimeFileResponse>(
            await response.Content.ReadAsStringAsync(cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (string.IsNullOrWhiteSpace(payload?.ContentBase64))
            return null;

        return (
            Convert.FromBase64String(payload.ContentBase64),
            string.IsNullOrWhiteSpace(payload.ContentType) ? "application/octet-stream" : payload.ContentType);
    }

    private async Task<HttpResponseMessage> SendTunnelRequestAsync(
        Runner runner,
        string path,
        string body,
        CancellationToken cancellationToken)
    {
        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException($"Runner '{runner.Name}' is not connected");

        if (!string.IsNullOrWhiteSpace(runner.ApiInstanceId)
            && !string.Equals(runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Runner '{runner.Name}' belongs to another API instance.");
        }

        using var lease = tunnels.TryAcquire(runner.Id)
            ?? throw new InvalidOperationException($"Runner '{runner.Name}' has no tunnel connection");
        using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeTunnelRequestSender.BuildUri(path, string.Empty))
        {
            Version = System.Net.HttpVersion.Version20,
            VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(TunnelHeaders.Destination, TunnelDestinations.FastApi);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(tunnelConfig.TimeoutSeconds));
        // Buffered here (not ResponseHeadersRead) so the lease can end with the call.
        return await lease.Invoker.SendAsync(request, timeout.Token) is { } response
            ? await Buffer(response, timeout.Token)
            : throw new InvalidOperationException("No response from the Runner.");
    }

    private static async Task<HttpResponseMessage> Buffer(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        return response;
    }

    private sealed class RunnerRuntimeFileResponse
    {
        public string? ContentBase64 { get; set; }
        public string? ContentType { get; set; }
    }
}
