using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

public static class LogsCommand
{
    public static Command Build(ConfigService config)
    {
        var targetArgument = new Argument<string?>("environment", () => null, "Environment slug; omit to use the only environment or choose interactively")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable prompts");
        var environmentOption = new Option<string?>("--env", "Use a named project environment");
        var command = new Command("logs", "Open runtime logs for an environment")
        {
            targetArgument, jsonOption, nonInteractiveOption, environmentOption
        };

        command.SetHandler(async (InvocationContext context) =>
        {
            var targetSlug = context.ParseResult.GetValueForArgument(targetArgument);
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var nonInteractive = CliInteraction.IsNonInteractive(
                context.ParseResult.GetValueForOption(nonInteractiveOption), asJson);
            var environmentName = context.ParseResult.GetValueForOption(environmentOption);
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var targetService = new DeploymentTargetService(config);
                var (_, targets) = await targetService.ListAsync(cwd, environmentName, context.GetCancellationToken());
                var target = DeploymentTargetService.SelectEnvironment(targets, targetSlug, nonInteractive);
                var resolved = config.ResolveContext(config.TryLoadProjectConfig(cwd), cwd, environmentName);
                var url = CliAppUrlBuilder.BuildStudioLogsUrl(resolved.Server, resolved.AppId, target.Slug);
                if (asJson)
                {
                    CliJson.Write(new { success = true, environment = target.Slug, url });
                }
                else
                {
                    Console.WriteLine($"Runtime logs · {target.Name}");
                    CliBrowserService.OpenUrl(url);
                    Console.WriteLine($"Opened: {url}");
                }
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                if (asJson) CliJson.Write(new { success = false, error = ex.Message });
                else Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = 1;
            }
        });
        return command;
    }
}
