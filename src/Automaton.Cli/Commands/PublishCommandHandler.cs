using Automaton.Cli.Models;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

internal static class PublishCommandHandler
{
    internal static async Task HandleAsync(
        ConfigService config,
        string? slug,
        bool makePrivate,
        bool makePublic,
        bool forkable,
        bool yes,
        bool open,
        bool asJson,
        bool nonInteractiveFlag)
    {
        var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag, asJson);
        if (WriteConflictingFlagsFailure(makePrivate, makePublic, asJson, nonInteractive))
        {
            Environment.ExitCode = 1;
            return;
        }

        var result = await TryPublishAsync(
            config, slug, makePrivate, makePublic, forkable, yes, nonInteractive, asJson);
        PublishCommand.WriteResult(result, asJson, nonInteractive);
        OpenPublishedResult(result, open);
        Environment.ExitCode = result.Success ? 0 : 1;
    }

    private static bool WriteConflictingFlagsFailure(
        bool makePrivate, bool makePublic, bool asJson, bool nonInteractive)
    {
        if (!makePrivate || !makePublic) return false;
        var failure = new PublishResult
        {
            Success = false,
            Summary = "Publish failed",
            Checks = [new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.Publish, false, "choose either --public or --private, not both")],
            NextSteps = ["Rerun automaton publish with at most one access flag"]
        };
        PublishCommand.WriteResult(failure, asJson, nonInteractive);
        return true;
    }

    private static async Task<PublishResult> TryPublishAsync(
        ConfigService config,
        string? slug,
        bool makePrivate,
        bool makePublic,
        bool forkable,
        bool yes,
        bool nonInteractive,
        bool asJson)
    {
        try
        {
            var result = await new PublishService(config).PublishAsync(
                Directory.GetCurrentDirectory(),
                slug,
                ResolveAuthenticationRequirement(makePrivate, makePublic),
                forkable ? true : null,
                yes,
                nonInteractive: nonInteractive);
            return result;
        }
        catch (Exception exception)
        {
            return CreateFailure(exception.Message);
        }
    }

    private static bool? ResolveAuthenticationRequirement(bool makePrivate, bool makePublic)
        => makePrivate ? true : makePublic ? false : null;

    private static PublishResult CreateFailure(string message)
        => new()
        {
            Success = false,
            Summary = "Publish failed",
            Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, message)],
            NextSteps = ["Run automaton whoami to confirm the current server and identity"]
        };

    private static void OpenPublishedResult(PublishResult result, bool open)
    {
        if (result.Success && open && !string.IsNullOrWhiteSpace(result.Url))
        {
            CliBrowserService.OpenUrl(result.Url);
        }
    }
}
