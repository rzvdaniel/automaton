using Automaton.Cli.Models;
using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

public static class OpenCommand
{
    public static Command Build(ConfigService config)
    {
        var publicOption = new Option<bool>("--public", "Open the published public app URL");
        var privateOption = new Option<bool>("--private", "Open the app in Studio (legacy alias)");
        var targetArgument = new Argument<string?>("environment", () => null, "Environment slug to open; omit for Live") { Arity = ArgumentArity.ZeroOrOne };
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");

        var cmd = new Command("open", "Open the current app in the browser")
        {
            targetArgument,
            publicOption,
            privateOption,
            environmentOption
        };

        cmd.SetHandler(context => HandleOpenAsync(
            config,
            context,
            targetArgument,
            publicOption,
            privateOption,
            environmentOption));

        return cmd;
    }

    private static async Task HandleOpenAsync(
        ConfigService config,
        InvocationContext context,
        Argument<string?> targetArgument,
        Option<bool> publicOption,
        Option<bool> privateOption,
        Option<string?> environmentOption)
    {
        var targetSlug = context.ParseResult.GetValueForArgument(targetArgument);
        var openPublic = context.ParseResult.GetValueForOption(publicOption);
        var openPrivate = context.ParseResult.GetValueForOption(privateOption);
        var environmentName = context.ParseResult.GetValueForOption(environmentOption);
        if (openPublic && openPrivate)
        {
            Fail(context, "choose either --public or --private, not both");
            return;
        }

        try
        {
            var url = await ResolveAppUrlAsync(config, targetSlug, environmentName, openPublic);
            if (url == null)
            {
                context.ExitCode = 1;
                return;
            }

            CliBrowserService.OpenUrl(url);
            Console.WriteLine($"Opened: {url}");
            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Fail(context, ex.Message);
        }
    }

    private static async Task<string?> ResolveAppUrlAsync(
        ConfigService config,
        string? targetSlug,
        string? environmentName,
        bool openPublic)
    {
        var cwd = Directory.GetCurrentDirectory();
        var appConfig = config.TryLoadProjectConfig(cwd);
        var resolved = config.ResolveContext(appConfig, cwd, environmentName);
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appLinkCheck = CliCommandChecks.ValidateProjectLink(
            File.Exists(Path.Combine(cwd, "automaton.json")),
            appConfig,
            resolved.AppId,
            nextSteps);
        var credentialsCheck = CliCommandChecks.ValidateCredentials(resolved.Server, resolved.ApiKey, nextSteps);
        if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null
            || string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            WriteOpenDiagnostics(appLinkCheck, credentialsCheck, nextSteps);
            return null;
        }

        using var client = new AutomatonApiClient(resolved.Server, resolved.ApiKey);
        var app = await client.GetAppAsync(resolved.AppId);
        var target = string.IsNullOrWhiteSpace(targetSlug)
            ? null
            : await new DeploymentTargetService(config).ResolveAsync(cwd, targetSlug, environmentName);

        return ResolvePublicOrStudioUrl(
            app,
            resolved.Server,
            resolved.AppId,
            target,
            openPublic);
    }

    private static string ResolvePublicOrStudioUrl(
        System.Text.Json.JsonElement app,
        string server,
        string appId,
        DeploymentTargetInfo? target,
        bool openPublic)
    {
        if (!openPublic)
        {
            var studioUrl = CliAppUrlBuilder.BuildStudioApplicationUrl(server, appId);
            return target == null
                ? studioUrl
                : $"{studioUrl}?target={Uri.EscapeDataString(target.Slug)}";
        }

        if (target != null)
            throw new InvalidOperationException("Public URLs always open the Live environment. Use 'automaton open <environment>' for its preview, or 'automaton open --public' for the published Live app.");

        var isPublished = app.TryGetProperty("isPublished", out var publishedProperty)
            && publishedProperty.GetBoolean();
        var publicSlug = app.TryGetProperty("publicUrl", out var publicUrlProperty)
            ? publicUrlProperty.GetString()
            : null;
        if (!isPublished || string.IsNullOrWhiteSpace(publicSlug))
            throw new InvalidOperationException("this app is not published yet");

        return CliAppUrlBuilder.BuildPublishedUrl(server, publicSlug);
    }

    private static void WriteOpenDiagnostics(
        CommandDiagnosticCheck appLinkCheck,
        CommandDiagnosticCheck credentialsCheck,
        IEnumerable<string> nextSteps)
    {
        CliDiagnosticsWriter.Write(new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Open failed",
            Checks = [appLinkCheck, credentialsCheck],
            NextSteps = nextSteps.ToList()
        });
    }

    private static void Fail(InvocationContext context, string message)
    {
        WriteFailure(message);
        context.ExitCode = 1;
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
