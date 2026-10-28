using Automaton.Cli.Models;
using Spectre.Console;

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
                candidate.IsDefault ? $"{candidate.Slug} (Live)" : candidate.Slug));
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(normalized)
                ? $"This app has no Live environment. Available environments: {available}."
                : $"Environment '{normalized}' was not found. Available environments: {available}.");
    }

    public static IReadOnlyList<DeploymentTargetInfo> GetDeployableTargets(
        IReadOnlyList<DeploymentTargetInfo> targets) => targets
        .Where(IsDeployable)
        .ToList();

    public static bool IsDeployable(DeploymentTargetInfo environment) =>
        environment.Status != "Deploying" &&
        (environment.Runner is null && environment.IsDefault || environment.Runner is { IsOnline: true });

    public static void EnsureDeployable(DeploymentTargetInfo environment)
    {
        if (!IsDeployable(environment))
        {
            throw new InvalidOperationException($"Environment '{environment.Slug}' is not deployable in its current state ({environment.Status}).");
        }
    }

    public static DeploymentTargetInfo SelectEnvironment(
        IReadOnlyList<DeploymentTargetInfo> environments,
        string? slug,
        bool nonInteractive)
    {
        if (!string.IsNullOrWhiteSpace(slug)) return Select(environments, slug);
        if (environments.Count == 1) return environments[0];
        if (environments.Count == 0) throw new InvalidOperationException("This app has no environments.");
        if (nonInteractive)
        {
            var slugs = string.Join(", ", environments.Select(environment => environment.Slug));
            throw new InvalidOperationException($"Multiple environments are available. Specify one explicitly: {slugs}.");
        }
        var labels = environments.Select(Format).ToList();
        var prompt = new SelectionPrompt<string>().AddChoices(labels);
        prompt.Title = "Select an environment";
        return environments[labels.IndexOf(AnsiConsole.Prompt(prompt))];
    }

    public static DeploymentTargetInfo SelectForDeploy(
        IReadOnlyList<DeploymentTargetInfo> targets,
        string? slug,
        bool nonInteractive)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var explicitEnvironment = Select(targets, slug);
            EnsureDeployable(explicitEnvironment);
            return explicitEnvironment;
        }

        var deployable = GetDeployableTargets(targets);
        if (deployable.Count == 1)
        {
            return deployable[0];
        }
        if (deployable.Count == 0)
        {
            throw new InvalidOperationException("This app has no deployable environments.");
        }
        if (nonInteractive)
        {
            var commands = string.Join(", ", deployable.Select(target => $"automaton deploy {target.Slug}"));
            throw new InvalidOperationException($"Multiple deployable environments are available. Choose one explicitly: {commands}.");
        }

        var labels = deployable.Select(Format).ToList();
        var prompt = new SelectionPrompt<string>()
            .AddChoices(labels);
        prompt.Title = "Select an environment";
        var selected = Spectre.Console.AnsiConsole.Prompt(prompt);
        return deployable[labels.IndexOf(selected)];
    }

    public static string Format(DeploymentTargetInfo target)
    {
        var destination = FormatDestination(target.Runner);
        var status = target.Runner is { IsOnline: false } ? "Runner offline" : target.Status;
        return $"{target.Name} ({target.Slug}) · {(target.IsDefault ? "Live" : "Additional")} · {destination} · {status}";
    }

    public static string FormatDestination(DeploymentTargetRunnerInfo? runner) =>
        runner is null ? "No runtime assigned" : "Machine Management Runner";

    public async Task<DeploymentTargetInfo> AddAsync(
        string directory,
        string slug,
        string? runnerReference,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var (client, appId, _) = CreateClient(directory, environmentName);
        using (client)
        {
            var runner = string.IsNullOrWhiteSpace(runnerReference)
                ? null
                : await ResolveRunnerAsync(client, runnerReference, cancellationToken);
            var normalizedSlug = slug.Trim().ToLowerInvariant();
            return await client.CreateDeploymentTargetAsync(
                appId,
                normalizedSlug,
                normalizedSlug,
                runner,
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
            var runner = await ResolveRunnerAsync(
                client, runnerReference, cancellationToken);
            await client.AssignDeploymentTargetRunnerAsync(
                appId, target.Id, runner, ct: cancellationToken);
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

    private static async Task<string> ResolveRunnerAsync(
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
        var match = matches[0];
        return match.GetProperty("id").GetString()!;
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
