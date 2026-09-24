using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Automaton.Cli;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        var config = new ConfigService();
        if (IsVersionRequest(args))
        {
            new VersionService().WriteVersion(Console.Out);
            return 0;
        }

        var root = CliRootCommandBuilder.Build(config);
        var parser = CliParserBuilder.Build(root, config, args);
        return await parser.InvokeAsync(args);
    }

    private static bool IsVersionRequest(string[] args) =>
        args.Length == 1 && (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase)
            || string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase));
}

internal static class CliRootCommandBuilder
{
    public static RootCommand Build(ConfigService config)
    {
        var root = new RootCommand("Automaton CLI — create, deploy, and publish Automaton apps");
        CliCommandRegistration.AddCreationCommands(root, config);
        CliCommandRegistration.AddProjectCommands(root, config);
        CliCommandRegistration.AddDeploymentCommands(root, config);
        CliCommandRegistration.AddRunnerCommands(root, config);
        root.Name = "automaton";
        ConfigureUnmatchedTokenErrors(root);
        return root;
    }

    private static void ConfigureUnmatchedTokenErrors(Command command)
    {
        command.TreatUnmatchedTokensAsErrors = true;
        foreach (var subcommand in command.Subcommands)
        {
            ConfigureUnmatchedTokenErrors(subcommand);
        }
    }
}

internal static class CliCommandRegistration
{
    public static void AddCreationCommands(RootCommand root, ConfigService config)
    {
        root.AddCommand(NewCommand.Build(config));
        root.AddCommand(SyncAiCommand.Build(config));
        root.AddCommand(LoginCommand.Build(config));
        root.AddCommand(LogoutCommand.Build(config));
        root.AddCommand(WhoAmICommand.Build(config));
        root.AddCommand(CreateCommand.Build(config));
    }

    public static void AddProjectCommands(RootCommand root, ConfigService config)
    {
        root.AddCommand(StatusCommand.Build(config));
        root.AddCommand(CurrentRunnerCommand.Build(config));
        root.AddCommand(ValidateCommand.Build(config));
        root.AddCommand(ActionCommand.Build(config));
        root.AddCommand(InitCommand.Build(config));
        root.AddCommand(LinkCommand.Build(config));
    }

    public static void AddDeploymentCommands(RootCommand root, ConfigService config)
    {
        root.AddCommand(DeployCommand.Build(config));
        root.AddCommand(OpenCommand.Build(config));
        root.AddCommand(LogsCommand.Build(config));
        root.AddCommand(PullCommand.Build(config));
        root.AddCommand(PublishCommand.Build(config));
    }

    public static void AddRunnerCommands(RootCommand root, ConfigService config)
    {
        var runner = RunnerCommand.Build(config);
        root.AddCommand(runner);
        var installAlias = new Command("install", "Install local Automaton components");
        installAlias.AddCommand(RunnerCommand.Build(config));
        root.AddCommand(installAlias);
        root.AddCommand(RunnersCommand.Build(config));
        root.AddCommand(SetRunnerCommand.Build(config));
        root.AddCommand(ConfigCommand.Build(config));
        root.AddCommand(TargetCommand.Build(config));
    }
}

internal static class CliParserBuilder
{
    public static Parser Build(RootCommand root, ConfigService config, string[] args)
    {
        var authorizationCompletion = new CliAuthorizationCompletionService(config);
        return new CommandLineBuilder(root)
            .UseHelp()
            .UseParseErrorReporting()
            .AddMiddleware((context, next) => InvokeMiddlewareAsync(context, next, args, authorizationCompletion), MiddlewareOrder.Default)
            .Build();
    }

    private static async Task InvokeMiddlewareAsync(
        InvocationContext context,
        Func<InvocationContext, Task> next,
        string[] args,
        CliAuthorizationCompletionService authorizationCompletion)
    {
        if (HasInvalidArguments(context, args))
        {
            WriteInvalidArgumentError(context);
            return;
        }

        if (RequiresAuthorizationCompletion(context)
            && !await authorizationCompletion.TryCompletePendingAsync(
                Directory.GetCurrentDirectory(),
                cancellationToken: context.GetCancellationToken()))
        {
            Console.Error.WriteLine("Warning: CLI credential is saved, but server acknowledgement is pending. Automaton will retry automatically.");
        }

        await next(context);
    }

    private static bool HasInvalidArguments(InvocationContext context, string[] args) =>
        context.ParseResult.UnmatchedTokens.Count > 0
        || CliOptionValidator.HasUnsupportedOptions(args, context.ParseResult.CommandResult.Command);

    private static bool RequiresAuthorizationCompletion(InvocationContext context) =>
        context.ParseResult.CommandResult.Command.Name is not ("login" or "logout" or "config" or "help");

    private static void WriteInvalidArgumentError(InvocationContext context)
    {
        Console.Error.WriteLine("Unrecognized argument or option. Run 'automaton <command> --help' to see supported inputs.");
        context.ExitCode = 2;
    }
}
