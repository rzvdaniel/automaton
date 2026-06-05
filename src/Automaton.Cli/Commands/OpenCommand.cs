using Automaton.Cli.Models;
using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class OpenCommand
{
    public static Command Build(ConfigService config)
    {
        var publicOption = new Option<bool>("--public", "Open the published public app URL");
        var privateOption = new Option<bool>("--private", "Open the private deployed app URL");

        var cmd = new Command("open", "Open the current app in the browser")
        {
            publicOption,
            privateOption
        };

        cmd.SetHandler(async (bool openPublic, bool openPrivate) =>
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
                var (server, apiKey) = config.ResolveCredentials(appConfig, cwd);
                var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, nextSteps);
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
                var app = await client.GetAppAsync(appConfig.AppId);
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
                    var launchCode = await client.CreatePrivateLaunchAsync(appConfig.AppId);
                    url = CliAppUrlBuilder.BuildPrivateLaunchUrl(server!, appConfig.AppId, launchCode);
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
        }, publicOption, privateOption);

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
