using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using System.CommandLine;

var config = new ConfigService();

var root = new RootCommand("Automaton CLI — create, deploy, and publish Automaton apps")
{
    LoginCommand.Build(config),
    WhoAmICommand.Build(config),
    CreateCommand.Build(config),
    StatusCommand.Build(config),
    CurrentRunnerCommand.Build(config),
    ValidateCommand.Build(config),
    EnsureLocalRunnerCommand.Build(config),
    InitCommand.Build(config),
    LinkCommand.Build(config),
    DeployCommand.Build(config),
    PullCommand.Build(config),
    PublishCommand.Build(config),
    RunnersCommand.Build(config),
    SetRunnerCommand.Build(config),
    ConfigCommand.Build(config),
};

root.Name = "automaton";

return await root.InvokeAsync(args);
