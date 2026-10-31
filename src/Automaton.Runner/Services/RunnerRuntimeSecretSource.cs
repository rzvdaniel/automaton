using System.Net.Http.Json;

namespace Automaton.Runner.Console.Services;

/// <summary>Supplies an app's current secrets each time the Runner starts one of its runtimes.</summary>
public interface IRuntimeSecretSource
{
    Task<IReadOnlyDictionary<string, string>> GetAsync(Guid appId, Guid deploymentTargetId, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches runtime secrets from the API with this Runner's credential. The API returns them only for
/// targets assigned to this Runner. Failures propagate, so a runtime is never started without its secrets.
/// </summary>
internal sealed class RunnerRuntimeSecretSource(FileStorageService storage) : IRuntimeSecretSource
{
    public async Task<IReadOnlyDictionary<string, string>> GetAsync(Guid appId, Guid deploymentTargetId, CancellationToken cancellationToken)
    {
        var key = await storage.LoadApiKeyAsync();
        var url = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL") ?? await storage.LoadServerUrlAsync();
        if (key is null || !Uri.TryCreate(url, UriKind.Absolute, out var address) || !Guid.TryParse(storage.GetRunnerId(), out var runnerId))
            throw new InvalidOperationException("The Runner is not enrolled; app secrets cannot be loaded.");
        using var api = new RunnerExecutionApiClient(address, key, runnerId);
        return await api.RuntimeSecretsAsync(appId, deploymentTargetId, cancellationToken);
    }
}
