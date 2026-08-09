using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class DeploymentTargetService(ConfigService config)
{
    public async Task<(string AppName, IReadOnlyList<DeploymentTargetInfo> Targets)> ListAsync(
        string directory,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var (client, appId, appName) = CreateClient(directory, environmentName);
        using (client)
        {
            return (
                appName,
                await client.ListDeploymentTargetsAsync(appId, cancellationToken));
        }
    }

    public async Task<DeploymentTargetInfo> ResolveAsync(
        string directory,
        string? slug,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var (_, targets) = await ListAsync(directory, environmentName, cancellationToken);
        return Select(targets, slug);
    }

    public static DeploymentTargetInfo Select(
        IReadOnlyList<DeploymentTargetInfo> targets,
        string? slug)
    {
        var normalized = slug?.Trim().ToLowerInvariant();
        var target = string.IsNullOrWhiteSpace(normalized)
            ? targets.SingleOrDefault(candidate => candidate.IsDefault)
            : targets.SingleOrDefault(candidate =>
                string.Equals(candidate.Slug, normalized, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            return target;
        }

        var available = targets.Count == 0
            ? "(none)"
            : string.Join(", ", targets.Select(candidate =>
                candidate.IsDefault ? $"{candidate.Slug} (default)" : candidate.Slug));
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(normalized)
                ? $"This app has no default deployment target. Available targets: {available}."
                : $"Deployment target '{normalized}' was not found. Available targets: {available}.");
    }

    public async Task<DeploymentTargetInfo> AddAsync(
        string directory,
        string slug,
        string runnerReference,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var (client, appId, _) = CreateClient(directory, environmentName);
        using (client)
        {
            var runnerId = await ResolveRunnerIdAsync(
                client, runnerReference, cancellationToken);
            var normalizedSlug = slug.Trim().ToLowerInvariant();
            return await client.CreateDeploymentTargetAsync(
                appId,
                normalizedSlug,
                normalizedSlug,
                runnerId,
                cancellationToken);
        }
    }

    public async Task<DeploymentTargetInfo> AssignRunnerAsync(
        string directory,
        DeploymentTargetInfo target,
        string runnerReference,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var (client, appId, _) = CreateClient(directory, environmentName);
        using (client)
        {
            var runnerId = await ResolveRunnerIdAsync(
                client, runnerReference, cancellationToken);
            await client.AssignDeploymentTargetRunnerAsync(
                appId, target.Id, runnerId, ct: cancellationToken);
        }
        return await ResolveAsync(directory, target.Slug, environmentName, cancellationToken);
    }

    public async Task<DeploymentTargetInfo> ClearRunnerAsync(
        string directory,
        DeploymentTargetInfo target,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var (client, appId, _) = CreateClient(directory, environmentName);
        using (client)
        {
            await client.AssignDeploymentTargetRunnerAsync(
                appId, target.Id, null, ct: cancellationToken);
        }
        return await ResolveAsync(directory, target.Slug, environmentName, cancellationToken);
    }

    public async Task RemoveAsync(
        string directory,
        string slug,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveAsync(directory, slug, environmentName, cancellationToken);
        var (client, appId, _) = CreateClient(directory, environmentName);
        using (client)
        {
            await client.DeleteDeploymentTargetAsync(appId, target.Id, cancellationToken);
        }
    }

    public async Task DefaultAsync(
        string directory,
        string slug,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveAsync(directory, slug, environmentName, cancellationToken);
        var (client, appId, _) = CreateClient(directory, environmentName);
        using (client)
        {
            await client.MakeDeploymentTargetDefaultAsync(appId, target.Id, cancellationToken);
        }
    }

    private (AutomatonApiClient Client, string AppId, string AppName) CreateClient(
        string directory,
        string? environmentName)
    {
        var project = config.TryLoadProjectConfig(directory)
            ?? throw new InvalidOperationException("automaton.json was not found.");
        var resolved = config.ResolveContext(project, directory, environmentName);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
        {
            throw new InvalidOperationException("The project is not linked to an Automaton app.");
        }
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            throw new InvalidOperationException("Automaton server credentials are not configured.");
        }
        return (
            new AutomatonApiClient(resolved.Server, resolved.ApiKey),
            resolved.AppId,
            project.Name ?? resolved.AppId);
    }

    private static async Task<string> ResolveRunnerIdAsync(
        AutomatonApiClient client,
        string runnerReference,
        CancellationToken cancellationToken)
    {
        var runners = await client.ListRunnersAsync(cancellationToken);
        var matches = runners.ValueKind == System.Text.Json.JsonValueKind.Array
            ? runners.EnumerateArray()
                .Where(runner =>
                {
                    var id = runner.TryGetProperty("id", out var idProperty)
                        ? idProperty.GetString()
                        : null;
                    var name = runner.TryGetProperty("name", out var nameProperty)
                        ? nameProperty.GetString()
                        : null;
                    return string.Equals(id, runnerReference, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, runnerReference, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(
                               ToSlug(name),
                               runnerReference.Trim().ToLowerInvariant(),
                               StringComparison.Ordinal);
                })
                .ToList()
            : [];
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Runner '{runnerReference}' was not found. Run 'automaton runners' to list available Runners.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Several Runners match '{runnerReference}'. Use the Runner ID.");
        }
        return matches[0].GetProperty("id").GetString()!;
    }

    private static string? ToSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var slug = new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }
}
