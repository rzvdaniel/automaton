using Automaton.Core.Models;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HttpDeploymentRoutingTests
{
    private static ApplicationContract Contract(string kind)
    {
        var contract = new ApplicationContract();
        if (kind == "static")
        {
            contract.Build.Image = "test/build:1";
            contract.Build.Command = new ApplicationCommandSpec { Executable = "build" };
            contract.Build.OutputDirectory = "dist";
        }
        else if (kind == "compose")
        {
            contract.Compose = new ApplicationComposeSpec
            {
                PublicService = "web",
                Services = [new ApplicationComposeService { Name = "web", Image = "test/web:1", Port = 8080 }]
            };
        }
        return contract;
    }
}
