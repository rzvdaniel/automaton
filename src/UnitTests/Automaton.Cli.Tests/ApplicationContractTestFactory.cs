using Automaton.Core.Models;

namespace Automaton.Cli.Models;

internal static class ApplicationContractTestFactory
{
    public static ApplicationContract Create(string outputDirectory = "frontend/dist") => new()
    {
        Build = new ApplicationBuildSpec
        {
            Command = new ApplicationCommandSpec
            {
                Executable = "npm",
                Arguments = ["run", "build"],
                WorkingDirectory = "frontend"
            },
            OutputDirectory = outputDirectory
        },
        Runtime = new ApplicationRuntimeSpec
        {
            Command = new ApplicationCommandSpec
            {
                Executable = "serve",
                Arguments = [outputDirectory],
                WorkingDirectory = "."
            }
        }
    };
}
