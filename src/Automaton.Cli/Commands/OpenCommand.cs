using Automaton.Cli.Models;
using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class OpenCommand
{
    public static Command Build(ConfigService config)
    {
        var publicOption = new Option<bool>("--public", "Open the published public app URL");
        var privateOption = new Option<bool>("--private", "Open the app in Studio (legacy alias)");
        var targetArgument = new Argument<string?>("target", () => null, "Target slug to open; omit for Live") { Arity = ArgumentArity.ZeroOrOne };
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");

        var cmd = new Command("open", "Open the current app in the browser")
        {
            targetArgument,
            publicOption,
            privateOption,
            environmentOption
        };

        cmd.SetHandler(async (string? targetSlug, bool openPublic, bool openPrivate, string? environmentName) =>
        {
            if (openPublic && openPrivate)
            {
                WriteFailure("choose either --public or --private, not both");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var resolved = config.ResolveContext(appConfig, cwd, environmentName);
                var server = resolved.Server;
                var apiKey = resolved.ApiKey;
                var appId = resolved.AppId;
                var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, appId, nextSteps);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);

                if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    CliDiagnosticsWriter.Write(new CommandDiagnosticResult
                    {
                        Success = false,
                        Summary = "Open failed",
                        Checks = [appLinkCheck, credentialsCheck],
                        NextSteps = nextSteps.ToList()
                    });
                    Environment.ExitCode = 1;
                    return;
                }

                using var client = new AutomatonApiClient(server!, apiKey!);
                var app = await client.GetAppAsync(appId!);
                DeploymentTargetInfo? target = null;
                if (!string.IsNullOrWhiteSpace(targetSlug))
                {
                    target = await new DeploymentTargetService(config).ResolveAsync(cwd, targetSlug, environmentName);
                }
                var isPublished = app.TryGetProperty("isPublished", out var publishedProperty) && publishedProperty.GetBoolean();
                var publicSlug = app.TryGetProperty("publicUrl", out var publicUrlProperty) ? publicUrlProperty.GetString() : null;

                string url;
                if (openPublic)
                {
                    if (!isPublished || string.IsNullOrWhiteSpace(publicSlug))
                    {
                        WriteFailure("this app is not published yet");
                        Environment.ExitCode = 1;
                        return;
                    }

                    url = CliAppUrlBuilder.BuildPublishedUrl(server!, publicSlug);
                }
                else
                {
                    url = CliAppUrlBuilder.BuildStudioApplicationUrl(server!, appId);
                    if (target != null)
                    {
                        url += $"?target={Uri.EscapeDataString(target.Slug)}";
                    }
                }

                CliBrowserService.OpenUrl(url);
                Console.WriteLine($"Opened: {url}");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteFailure(ex.Message);
                Environment.ExitCode = 1;
            }
        }, targetArgument, publicOption, privateOption, environmentOption);

        return cmd;
    }

    private static void WriteFailure(string message)
    {
        CliDiagnosticsWriter.Write(new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Open failed",
            Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, false, message)],
            NextSteps = []
        });
    }
}
