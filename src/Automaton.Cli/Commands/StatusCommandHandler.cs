using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.Text.Json;

namespace Automaton.Cli.Commands;

internal static class StatusCommandHandler
{
    internal static async Task HandleAsync(ConfigService config, bool asJson, string? environmentName)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var context = ResolveContext(config, environmentName, checks, nextSteps);
            if (context == null)
            {
                WriteFailure(checks, nextSteps, asJson);
                Environment.ExitCode = 1;
                return;
            }

            var remote = await LoadRemoteAsync(context);
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.RemoteAccess, true,
                $"remote app loaded: {context.AppId}"));
            WriteSuccess(context, remote, checks, asJson);
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.RemoteAccess, false, exception.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            WriteFailure(checks, nextSteps, asJson);
            Environment.ExitCode = 1;
        }
    }

    private static ProjectContext? ResolveContext(
        ConfigService config,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var cwd = Directory.GetCurrentDirectory();
        var appConfig = config.TryLoadProjectConfig(cwd);
        var resolved = config.ResolveContext(appConfig, cwd, environmentName);
        var appLinkCheck = CliCommandChecks.ValidateProjectLink(
            File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, resolved.AppId, nextSteps);
        var credentialsCheck = CliCommandChecks.ValidateCredentials(
            resolved.Server, resolved.ApiKey, nextSteps);
        checks.Add(appLinkCheck);
        checks.Add(credentialsCheck);

        if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null ||
            string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            return null;
        }

        return new ProjectContext(
            cwd, appConfig, resolved.EnvironmentName, resolved.Server, resolved.ApiKey, resolved.AppId!);
    }

    private static async Task<RemoteStatus> LoadRemoteAsync(ProjectContext context)
    {
        using var client = new AutomatonApiClient(context.Server, context.ApiKey);
        var app = await client.GetAppAsync(context.AppId);
        var targets = await client.ListDeploymentTargetsAsync(context.AppId);
        var defaultTarget = targets.SingleOrDefault(target => target.IsDefault);
        var artifactDirectory = ResolveArtifactDirectory(context);
        var publicUrl = ReadString(app, "publicUrl");
        var isPublished = ReadBoolean(app, "isPublished");
        var liveUrl = isPublished && !string.IsNullOrWhiteSpace(publicUrl)
            ? CliAppUrlBuilder.BuildPublishedUrl(context.Server, publicUrl)
            : null;
        var openAppUrl = CliAppUrlBuilder.BuildRestrictedAppUrl(context.Server, context.AppId);
        var version = app.TryGetProperty("version", out var versionProperty)
            ? versionProperty.GetInt32()
            : 0;
        return new RemoteStatus(
            targets, defaultTarget, artifactDirectory, publicUrl, isPublished,
            liveUrl, openAppUrl, version);
    }

    private static string? ResolveArtifactDirectory(ProjectContext context)
    {
        var outputDirectory = context.Config.Application.Build.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory)) return null;
        try
        {
            return ZipService.ResolveDeployDirectory(context.Directory, outputDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            return null; // The assigned Runner builds the output; there is no local copy yet.
        }
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static bool ReadBoolean(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.GetBoolean();

    private static void WriteSuccess(
        ProjectContext context,
        RemoteStatus remote,
        IReadOnlyCollection<CommandDiagnosticCheck> checks,
        bool asJson)
    {
        if (asJson)
        {
            WriteJson(context, remote, checks);
            return;
        }

        AnsiConsole.Write(BuildTable(context, remote));
    }

    private static void WriteJson(
        ProjectContext context,
        RemoteStatus remote,
        IReadOnlyCollection<CommandDiagnosticCheck> checks)
    {
        CliJson.Write(new
        {
            success = true,
            summary = "Status loaded",
            checks = MapChecks(checks),
            nextSteps = Array.Empty<string>(),
            project = new
            {
                root = context.Directory,
                configPath = Path.Combine(context.Directory, "automaton.json"),
                appId = context.AppId,
                name = context.Config.Name,
                environment = context.EnvironmentName,
                server = context.Server,
                application = context.Config.Application,
                resolvedArtifactDirectory = remote.ArtifactDirectory
            },
            remote = new
            {
                remote.IsPublished,
                remote.PublicUrl,
                openAppUrl = remote.OpenAppUrl,
                liveUrl = remote.LiveUrl,
                defaultTarget = remote.DefaultTarget,
                remote.Targets,
                remote.Version
            }
        });
    }

    private static Table BuildTable(ProjectContext context, RemoteStatus remote)
    {
        var table = new Table();
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("app", $"{context.Config.Name} ({context.AppId})");
        table.AddRow("environment", string.IsNullOrWhiteSpace(context.EnvironmentName) ? "-" : context.EnvironmentName);
        table.AddRow("server", context.Server);
        table.AddRow("build executable", Optional(context.Config.Application.Build.Command?.Executable));
        table.AddRow("runtime executable", Optional(context.Config.Application.Runtime.Command?.Executable));
        table.AddRow("artifact directory", remote.ArtifactDirectory == null
            ? "-"
            : Path.GetRelativePath(context.Directory, remote.ArtifactDirectory));
        table.AddRow("Live target", FormatDefaultTarget(remote.DefaultTarget));
        table.AddRow("targets", remote.Targets.Count.ToString());
        table.AddRow("open app", Optional(remote.OpenAppUrl));
        table.AddRow("published", remote.IsPublished ? "yes" : "no");
        table.AddRow("public url", remote.LiveUrl ?? "-");
        return table;
    }

    private static string FormatDefaultTarget(DeploymentTargetInfo? target)
        => target == null ? "-" : $"{target.Slug} → {target.Runner?.Name ?? "unassigned"}";

    private static string Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static IEnumerable<object> MapChecks(IEnumerable<CommandDiagnosticCheck> checks)
        => checks.Select(check => new
        {
            name = check.Name,
            success = check.Success,
            warning = check.IsWarning,
            error = check.Message
        });

    private static void WriteFailure(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        bool asJson)
    {
        var result = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Status failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList()
        };
        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = result.Summary,
                checks = MapChecks(result.Checks),
                nextSteps = result.NextSteps
            });
            return;
        }

        CliDiagnosticsWriter.Write(result);
    }

    private sealed record ProjectContext(
        string Directory,
        AutomatonConfig Config,
        string? EnvironmentName,
        string Server,
        string ApiKey,
        string AppId);

    private sealed record RemoteStatus(
        IReadOnlyList<DeploymentTargetInfo> Targets,
        DeploymentTargetInfo? DefaultTarget,
        string? ArtifactDirectory,
        string? PublicUrl,
        bool IsPublished,
        string? LiveUrl,
        string? OpenAppUrl,
        int Version);
}
