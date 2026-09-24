using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class StatusCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");
        var command = new Command("status", "Show the current linked app, deploy settings, and publish state")
        {
            jsonOption,
            environmentOption
        };

        var handler = new StatusCommandHandler(config);
        command.SetHandler(handler.ExecuteAsync, jsonOption, environmentOption);
        return command;
    }
}

internal sealed class StatusCommandHandler(ConfigService config)
{
    public async Task ExecuteAsync(bool asJson, string? environmentName)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var request = ResolveRequest(environmentName, checks, nextSteps);
            if (request is null)
            {
                WriteFailure(asJson, checks, nextSteps);
                return;
            }

            var snapshot = await LoadSnapshotAsync(request);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {request.AppId}"));
            WriteSuccess(asJson, request, snapshot, checks);
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, false, exception.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            WriteFailure(asJson, checks, nextSteps);
        }
    }

    private StatusRequest? ResolveRequest(
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var cwd = Directory.GetCurrentDirectory();
        var appConfig = config.TryLoadProjectConfig(cwd);
        var resolved = config.ResolveContext(appConfig, cwd, environmentName);
        var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, resolved.AppId, nextSteps);
        var credentialsCheck = CliCommandChecks.ValidateCredentials(resolved.Server, resolved.ApiKey, nextSteps);
        checks.Add(appLinkCheck);
        checks.Add(credentialsCheck);

        if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig is null
            || string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            Environment.ExitCode = 1;
            return null;
        }

        return new StatusRequest(cwd, appConfig, resolved.AppId!, resolved.Server!, resolved.ApiKey!, resolved.EnvironmentName);
    }

    private static async Task<StatusSnapshot> LoadSnapshotAsync(StatusRequest request)
    {
        using var client = new AutomatonApiClient(request.Server, request.ApiKey);
        var app = await client.GetAppAsync(request.AppId);
        var targets = await client.ListDeploymentTargetsAsync(request.AppId);
        var defaultTarget = targets.SingleOrDefault(target => target.IsDefault);
        var artifactDirectory = string.IsNullOrWhiteSpace(request.Config.Application.Build.OutputDirectory)
            ? null
            : ZipService.ResolveDeployDirectory(request.Directory, request.Config.Application.Build.OutputDirectory);
        var publicUrl = GetString(app, "publicUrl");
        var isPublished = app.TryGetProperty("isPublished", out var publishedProperty) && publishedProperty.GetBoolean();
        var version = app.TryGetProperty("version", out var versionProperty) ? versionProperty.GetInt32() : 0;
        return new StatusSnapshot(
            artifactDirectory,
            publicUrl,
            isPublished,
            isPublished && !string.IsNullOrWhiteSpace(publicUrl) ? CliAppUrlBuilder.BuildPublishedUrl(request.Server, publicUrl) : null,
            CliAppUrlBuilder.BuildRestrictedAppUrl(request.Server, request.AppId),
            defaultTarget,
            targets,
            version);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) ? property.GetString() : null;

    private static void WriteSuccess(
        bool asJson,
        StatusRequest request,
        StatusSnapshot snapshot,
        IReadOnlyCollection<CommandDiagnosticCheck> checks)
    {
        if (asJson)
        {
            WriteJson(request, snapshot, checks);
            return;
        }

        WriteTable(request, snapshot);
    }

    private static void WriteJson(StatusRequest request, StatusSnapshot snapshot, IReadOnlyCollection<CommandDiagnosticCheck> checks)
    {
        CliJson.Write(new
        {
            success = true,
            summary = "Status loaded",
            checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = Array.Empty<string>(),
            project = new
            {
                root = request.Directory,
                configPath = Path.Combine(request.Directory, "automaton.json"),
                appId = request.AppId,
                name = request.Config.Name,
                environment = request.EnvironmentName,
                server = request.Server,
                application = request.Config.Application,
                resolvedArtifactDirectory = snapshot.ArtifactDirectory
            },
            remote = new
            {
                snapshot.IsPublished,
                snapshot.PublicUrl,
                openAppUrl = snapshot.OpenAppUrl,
                liveUrl = snapshot.LiveUrl,
                defaultTarget = snapshot.DefaultTarget,
                targets = snapshot.Targets,
                version = snapshot.Version
            }
        });
    }

    private static void WriteTable(StatusRequest request, StatusSnapshot snapshot)
    {
        var table = new Table();
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("app", $"{request.Config.Name} ({request.AppId})");
        table.AddRow("environment", string.IsNullOrWhiteSpace(request.EnvironmentName) ? "-" : request.EnvironmentName);
        table.AddRow("server", request.Server);
        table.AddRow("build executable", DisplayValue(request.Config.Application.Build.Command?.Executable));
        table.AddRow("runtime executable", DisplayValue(request.Config.Application.Runtime.Command?.Executable));
        table.AddRow("artifact directory", snapshot.ArtifactDirectory is null ? "-" : Path.GetRelativePath(request.Directory, snapshot.ArtifactDirectory));
        table.AddRow("Live target", FormatDefaultTarget(snapshot.DefaultTarget));
        table.AddRow("targets", snapshot.Targets.Count.ToString());
        table.AddRow("open app", DisplayValue(snapshot.OpenAppUrl));
        table.AddRow("published", snapshot.IsPublished ? "yes" : "no");
        table.AddRow("public url", DisplayValue(snapshot.LiveUrl));
        AnsiConsole.Write(table);
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string FormatDefaultTarget(DeploymentTargetInfo? target) => target is null
        ? "-"
        : $"{target.Slug} → {target.Runner?.Name ?? "unassigned"}";

    private static void WriteFailure(bool asJson, IReadOnlyCollection<CommandDiagnosticCheck> checks, IEnumerable<string> nextSteps)
    {
        Environment.ExitCode = 1;
        var failure = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Status failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList()
        };
        if (asJson)
        {
            CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
            return;
        }

        CliDiagnosticsWriter.Write(failure);
    }

    private sealed record StatusRequest(
        string Directory,
        AutomatonConfig Config,
        string AppId,
        string Server,
        string ApiKey,
        string? EnvironmentName);

    private sealed record StatusSnapshot(
        string? ArtifactDirectory,
        string? PublicUrl,
        bool IsPublished,
        string? LiveUrl,
        string? OpenAppUrl,
        DeploymentTargetInfo? DefaultTarget,
        IReadOnlyList<DeploymentTargetInfo> Targets,
        int Version);
}
