namespace Automaton.Infrastructure.Providers;

public interface IRunnerHostInfrastructureProvider
{
    string ProviderName { get; }

    Task<CreateRunnerHostServerResult> CreateServerAsync(
        CreateRunnerHostServerRequest request,
        CancellationToken cancellationToken);

    Task<RunnerHostServerInfo?> GetServerAsync(
        string providerResourceId,
        CancellationToken cancellationToken);

    Task RebootServerAsync(
        string providerResourceId,
        CancellationToken cancellationToken);

    Task DeleteServerAsync(
        string providerResourceId,
        CancellationToken cancellationToken);
}

public sealed record CreateRunnerHostServerRequest(
    string Name,
    string Region,
    string ServerType,
    string Image,
    IReadOnlyCollection<string> SshKeyIds,
    string CloudInit,
    string? PrivateNetworkId,
    IReadOnlyDictionary<string, string>? Labels = null);

public sealed record CreateRunnerHostServerResult(
    string ProviderResourceId,
    string? PublicIp,
    string? PrivateIp,
    string? ProviderRegion,
    string? ProviderServerType,
    string? ProviderImageId);

public sealed record RunnerHostServerInfo(
    string ProviderResourceId,
    string Status,
    string? PublicIp,
    string? PrivateIp,
    string? ProviderRegion,
    string? ProviderServerType,
    string? ProviderImageId);
