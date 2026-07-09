using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton publish [--slug my-app] [--yes]
///
/// Makes the latest deployed app publicly accessible and returns the public URL.
/// </summary>
public static class PublishCommand
{
    public static Command Build(ConfigService config)
    {
        var slugOption = new Option<string?>(["--slug", "-s"], "Public URL slug to use for publishing");
        var privateOption = new Option<bool>("--private", "Require Automaton login to access the published app");
        var publicOption = new Option<bool>("--public", "Allow public access without login");
        var forkableOption = new Option<bool>("--forkable", "Allow other users to fork this published app");
        var yesOption = new Option<bool>("--yes", "Publish without prompting for confirmation");
        var openOption = new Option<bool>("--open", "Open the published app in the browser after success");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable prompts and rich terminal rendering");

        var cmd = new Command("publish", "Make the latest deployed app publicly accessible")
        {
            slugOption,
            privateOption,
            publicOption,
            forkableOption,
            yesOption,
            openOption,
            jsonOption,
            nonInteractiveOption
        };

        cmd.SetHandler(async (string? slug, bool makePrivate, bool makePublic, bool forkable, bool yes, bool open, bool asJson, bool nonInteractiveFlag) =>
        {
            var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag, asJson);

            if (makePrivate && makePublic)
            {
                var failure = new PublishResult
                {
                    Success = false,
                    Summary = "Publish failed",
                    Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, "choose either --public or --private, not both")],
                    NextSteps = ["Rerun automaton publish with at most one access flag"]
                };
                WriteResult(failure, asJson, nonInteractive);
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var publishService = new PublishService(config);
                var result = await publishService.PublishAsync(
                    cwd,
                    slug,
                    makePrivate ? true : makePublic ? false : null,
                    forkable ? true : null,
                    yes,
                    nonInteractive: nonInteractive);
                WriteResult(result, asJson, nonInteractive);
                if (result.Success && open && !string.IsNullOrWhiteSpace(result.Url))
                {
                    CliBrowserService.OpenUrl(result.Url);
                }
                Environment.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                var failure = new PublishResult
                {
                    Success = false,
                    Summary = "Publish failed",
                    Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, ex.Message)],
                    NextSteps = ["Run automaton whoami to confirm the current server and identity"]
                };
                WriteResult(failure, asJson, nonInteractive);
                Environment.ExitCode = 1;
            }
        }, slugOption, privateOption, publicOption, forkableOption, yesOption, openOption, jsonOption, nonInteractiveOption);

        return cmd;
    }

    internal static void WriteResult(PublishResult result, bool asJson, bool nonInteractive = false)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = result.Success,
                cancelled = result.Cancelled,
                summary = result.Summary,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                appId = result.AppId,
                appName = result.AppName,
                slug = result.Slug,
                url = result.Url,
                studioAppUrl = result.StudioAppUrl,
                requiresAuthentication = result.RequiresAuthentication,
                isForkable = result.IsForkable
            });
            return;
        }

        if (!result.Success)
        {
            CliDiagnosticsWriter.Write(new CommandDiagnosticResult
            {
                Success = false,
                Summary = result.Summary,
                Checks = result.Checks,
                NextSteps = result.NextSteps
            }, nonInteractive: nonInteractive);
            return;
        }

        if (nonInteractive)
        {
            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                Console.WriteLine("Publish complete.");
                Console.WriteLine();
                Console.WriteLine($"Public app: {result.Url}");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Url))
        {
            AnsiConsole.MarkupLine("[green bold]Publish complete.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]Public app:[/] {result.Url}");
        }
    }
}
