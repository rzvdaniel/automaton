using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli;

internal static class CliCommandCatalog
{
    internal static RootCommand CreateRoot(ConfigService config)
    {
        var root = new RootCommand("Automaton CLI — create, deploy, and publish Automaton apps");
        AddCommands(root, config);
        root.Name = "automaton";
        return root;
    }

    private static void AddCommands(RootCommand root, ConfigService config)
    {
        root.AddCommand(NewCommand.Build(config));
        root.AddCommand(SyncAiCommand.Build(config));
        root.AddCommand(LoginCommand.Build(config));
        root.AddCommand(LogoutCommand.Build(config));
        root.AddCommand(WhoAmICommand.Build(config));
        root.AddCommand(CreateCommand.Build(config));
        root.AddCommand(StatusCommand.Build(config));
        root.AddCommand(CurrentRunnerCommand.Build(config));
        root.AddCommand(ValidateCommand.Build(config));
        root.AddCommand(ActionCommand.Build(config));
        root.AddCommand(SecretsCommand.Build(config));
        root.AddCommand(InitCommand.Build(config));
        root.AddCommand(LinkCommand.Build(config));
        root.AddCommand(DeployCommand.Build(config));
        root.AddCommand(OpenCommand.Build(config));
        root.AddCommand(LogsCommand.Build(config));
        root.AddCommand(PullCommand.Build(config));
        root.AddCommand(PublishCommand.Build(config));
        root.AddCommand(RunnersCommand.Build(config));
        root.AddCommand(SetRunnerCommand.Build(config));
        root.AddCommand(ConfigCommand.Build(config));
        root.AddCommand(TargetCommand.Build(config));
        root.AddCommand(ListingCommand.Build(config));
    }

}
