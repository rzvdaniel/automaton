using Automaton.Core.Enums;
using Automaton.Runner.Console.Models;

namespace Automaton.Runner.Console.Services;

public sealed record InfrastructureCommandIdentity(Guid RunnerHostId, Guid RunnerId)
{
    public static async Task<InfrastructureCommandIdentity> LoadAsync(FileStorageService storage)
    {
        var profile = await storage.LoadInstallationProfileAsync();
        var purpose = profile?.Purpose ?? Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var identity = Resolve(storage.GetRunnerId(), profile, purpose, Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_HOST_ID"));
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", purpose);
        return identity;
    }

    internal static InfrastructureCommandIdentity Resolve(
        string? configuredRunnerId, RunnerInstallationProfile? profile, string? purpose, string? hostId)
    {
        purpose = profile?.Purpose ?? purpose;
        hostId = profile != null ? profile.RunnerHostId?.ToString() : hostId;
        if (RunnerPurposes.Normalize(purpose) != RunnerPurposes.Infrastructure
            || !Guid.TryParse(configuredRunnerId, out var runnerId) || runnerId == Guid.Empty
            || !Guid.TryParse(hostId, out var runnerHostId) || runnerHostId == Guid.Empty)
            throw new InvalidOperationException("An enrolled Infrastructure Runner and Runner Host identity are required.");
        return new(runnerHostId, runnerId);
    }
}
