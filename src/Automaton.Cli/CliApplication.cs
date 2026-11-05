using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Automaton.Cli;

internal static class CliApplication
{
    internal static async Task<int> RunAsync(string[] args, ConfigService config)
    {
        if (TryWriteVersion(args)) return 0;
        var root = CliCommandCatalog.CreateRoot(config);
        ConfigureUnmatchedTokenErrors(root);
        var parser = BuildParser(root, args, config);
        return await parser.InvokeAsync(args);
    }

    private static bool TryWriteVersion(string[] args)
    {
        if (args.Length != 1 || (!string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase))) return false;
        new VersionService().WriteVersion(Console.Out);
        return true;
    }

    private static Parser BuildParser(RootCommand root, string[] args, ConfigService config)
    {
        var authorizationCompletion = new CliAuthorizationCompletionService(config);
        return new CommandLineBuilder(root)
            .UseHelp()
            .CancelOnProcessTermination()
            .UseParseErrorReporting()
            .AddMiddleware(async (context, next) =>
                await HandleInvocationAsync(context, next, args, config, authorizationCompletion), MiddlewareOrder.Default)
            .Build();
    }

    private static async Task HandleInvocationAsync(
        InvocationContext context,
        Func<InvocationContext, Task> next,
        string[] args,
        ConfigService config,
        CliAuthorizationCompletionService authorizationCompletion)
    {
        var profileOption = context.ParseResult.RootCommandResult.Command.Options
            .OfType<Option<string?>>().First(option => option.HasAlias("--profile"));
        config.ProfileOverride = context.ParseResult.GetValueForOption(profileOption);
        if (HasUnsupportedInput(context, args))
        {
            WriteUnsupportedInputError(context);
            return;
        }
        if (RequiresAuthorizationCompletion(context) && !await CompleteAuthorizationAsync(context, authorizationCompletion))
            WritePendingAuthorizationWarning();
        Environment.ExitCode = 0;
        try { await next(context); }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
            context.ExitCode = 1;
        }
        PropagateHandlerExitCode(context);
    }

    /// <summary>
    /// Handlers that set <see cref="Environment.ExitCode"/> instead of the invocation context would otherwise
    /// exit 0, because the value the invocation returns wins over the environment value.
    /// </summary>
    internal static void PropagateHandlerExitCode(InvocationContext context)
    {
        if (context.ExitCode == 0 && Environment.ExitCode != 0) context.ExitCode = Environment.ExitCode;
    }

    private static bool HasUnsupportedInput(InvocationContext context, string[] args)
        => context.ParseResult.UnmatchedTokens.Count > 0
            || CliOptionValidator.HasUnsupportedOptions(args, context.ParseResult.CommandResult.Command);

    private static void WriteUnsupportedInputError(InvocationContext context)
    {
        Console.Error.WriteLine("Unrecognized argument or option. Run 'automaton <command> --help' to see supported inputs.");
        context.ExitCode = 2;
    }

    private static bool RequiresAuthorizationCompletion(InvocationContext context)
        => context.ParseResult.CommandResult.Command.Name is not ("login" or "logout" or "config" or "profiles" or "help");

    private static Task<bool> CompleteAuthorizationAsync(
        InvocationContext context,
        CliAuthorizationCompletionService authorizationCompletion)
        => authorizationCompletion.TryCompletePendingAsync(
            Directory.GetCurrentDirectory(), cancellationToken: context.GetCancellationToken());

    private static void WritePendingAuthorizationWarning()
        => Console.Error.WriteLine("Warning: CLI credential is saved, but server acknowledgement is pending. Automaton will retry automatically.");

    private static void ConfigureUnmatchedTokenErrors(Command command)
    {
        command.TreatUnmatchedTokensAsErrors = true;
        foreach (var subcommand in command.Subcommands) ConfigureUnmatchedTokenErrors(subcommand);
    }
}
