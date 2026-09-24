using Automaton.Cli.Models;
using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

namespace Automaton.Cli.Commands;

internal static class OpenCommandHandler
{
    internal static async Task HandleAsync(
        ConfigService config,
        InvocationContext context,
        Argument<string?> targetArgument,
        Option<bool> publicOption,
        Option<bool> privateOption,
        Option<string?> environmentOption)
    {
        var options = ReadOptions(context, targetArgument, publicOption, privateOption, environmentOption);
        if (HasConflictingOptions(options))
        {
            WriteFailure("choose either --public or --private, not both");
            context.ExitCode = 1;
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(config, options);
            if (target == null)
            {
                context.ExitCode = 1;
                return;
            }
            var url = ResolveUrl(options, target);
            CliBrowserService.OpenUrl(url);
            Console.WriteLine($"Opened: {url}");
            context.ExitCode = 0;
        }
        catch (Exception exception)
        {
            WriteFailure(exception.Message);
            context.ExitCode = 1;
        }
    }

    private static OpenOptions ReadOptions(
        InvocationContext context,
        Argument<string?> targetArgument,
        Option<bool> publicOption,
        Option<bool> privateOption,
        Option<string?> environmentOption)
        => new(
            context.ParseResult.GetValueForArgument(targetArgument),
            context.ParseResult.GetValueForOption(publicOption),
            context.ParseResult.GetValueForOption(privateOption),
            context.ParseResult.GetValueForOption(environmentOption));

    private static bool HasConflictingOptions(OpenOptions options)
        => options.OpenPublic && options.OpenPrivate;

    private static async Task<OpenContext?> ResolveTargetAsync(ConfigService config, OpenOptions options)
    {
        var directory = Directory.GetCurrentDirectory();
        var appConfig = config.TryLoadProjectConfig(directory);
        var resolved = config.ResolveContext(appConfig, directory, options.EnvironmentName);
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appLinkCheck = CliCommandChecks.ValidateProjectLink(
            File.Exists(Path.Combine(directory, "automaton.json")), appConfig, resolved.AppId, nextSteps);
        var credentialsCheck = CliCommandChecks.ValidateCredentials(resolved.Server, resolved.ApiKey, nextSteps);
        if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null ||
            string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            CliDiagnosticsWriter.Write(new CommandDiagnosticResult
            {
                Success = false,
                Summary = "Open failed",
                Checks = [appLinkCheck, credentialsCheck],
                NextSteps = nextSteps.ToList()
            });
            return null;
        }

        using var client = new AutomatonApiClient(resolved.Server, resolved.ApiKey);
        var app = await client.GetAppAsync(resolved.AppId!);
        var target = string.IsNullOrWhiteSpace(options.TargetSlug)
            ? null
            : await new DeploymentTargetService(config).ResolveAsync(
                directory, options.TargetSlug, options.EnvironmentName);
        return new OpenContext(resolved.Server, resolved.AppId!, app, target);
    }

    private static string ResolveUrl(OpenOptions options, OpenContext context)
    {
        if (options.OpenPublic)
        {
            return BuildPublicUrl(context);
        }

        var url = CliAppUrlBuilder.BuildStudioApplicationUrl(context.Server, context.AppId);
        if (context.Target != null)
        {
            url += $"?target={Uri.EscapeDataString(context.Target.Slug)}";
        }
        return url;
    }

    private static string BuildPublicUrl(OpenContext context)
    {
        if (context.Target != null)
        {
            throw new InvalidOperationException(
                "Public URLs always open the Live environment. Use 'automaton open <environment>' for its preview, or 'automaton open --public' for the published Live app.");
        }

        var isPublished = context.App.TryGetProperty("isPublished", out var property) && property.GetBoolean();
        var publicSlug = context.App.TryGetProperty("publicUrl", out var slugProperty)
            ? slugProperty.GetString()
            : null;
        if (!isPublished || string.IsNullOrWhiteSpace(publicSlug))
        {
            throw new InvalidOperationException("this app is not published yet");
        }
        return CliAppUrlBuilder.BuildPublishedUrl(context.Server, publicSlug);
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

    private sealed record OpenOptions(
        string? TargetSlug, bool OpenPublic, bool OpenPrivate, string? EnvironmentName);

    private sealed record OpenContext(
        string Server, string AppId, JsonElement App, DeploymentTargetInfo? Target);

}
