using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

var config = new ConfigService();

if (args.Length == 1 && (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase)
    || string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase)))
{
    var versionService = new VersionService();
    versionService.WriteVersion(Console.Out);
    return 0;
}

var root = new RootCommand("Automaton CLI — create, deploy, and publish Automaton apps")
{
    NewCommand.Build(config),
    SyncAiCommand.Build(config),
    LoginCommand.Build(config),
    LogoutCommand.Build(config),
    WhoAmICommand.Build(config),
    CreateCommand.Build(config),
    StatusCommand.Build(config),
    CurrentRunnerCommand.Build(config),
    ValidateCommand.Build(config),
    InitCommand.Build(config),
    LinkCommand.Build(config),
    DeployCommand.Build(config),
    OpenCommand.Build(config),
    PullCommand.Build(config),
    PublishCommand.Build(config),
    RunnerCommand.Build(config),
    RunnersCommand.Build(config),
    SetRunnerCommand.Build(config),
    ConfigCommand.Build(config),
};

var installAlias = new Command("install", "Install local Automaton components");
installAlias.AddCommand(RunnerCommand.Build(config));
root.AddCommand(installAlias);

root.Name = "automaton";

var authorizationCompletion = new CliAuthorizationCompletionService(config);
var builder = new CommandLineBuilder(root)
    .AddMiddleware(async (context, next) =>
    {
        var commandName = context.ParseResult.CommandResult.Command.Name;
        if (commandName is not ("login" or "logout" or "config" or "help")
            && !await authorizationCompletion.TryCompletePendingAsync(
                Directory.GetCurrentDirectory(),
                cancellationToken: context.GetCancellationToken()))
        {
            Console.Error.WriteLine("Warning: CLI credential is saved, but server acknowledgement is pending. Automaton will retry automatically.");
        }

        await next(context);
    }, MiddlewareOrder.Default)
    .Build();

return await builder.InvokeAsync(args);
