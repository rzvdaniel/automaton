using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using System.CommandLine;

var config = new ConfigService();

var root = new RootCommand("Automaton CLI — create, deploy, and publish Automaton apps")
{
    InitCommand.Build(config),
    DeployCommand.Build(config),
    PullCommand.Build(config),
    PublishCommand.Build(config),
    RunnersCommand.Build(config),
    ConfigCommand.Build(config),
};

root.Name = "automaton";

return await root.InvokeAsync(args);
