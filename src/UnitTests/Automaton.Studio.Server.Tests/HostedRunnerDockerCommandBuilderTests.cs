using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services;

namespace Automaton.Studio.Server.Tests;

public class HostedRunnerDockerCommandBuilderTests
{
    [Test]
    public void BuildPreview_IncludesLimitsAndRedactsApiKey()
    {
        var runner = new HostedRunner
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name = "Hosted Runner abc123",
            ContainerName = "automaton-hosted-runner-abc123",
            CpuCores = 0.5m,
            MemoryMb = 768
        };

        var options = new HostedRunnerOptions
        {
            ServerUrl = "https://api.automaton.monster",
            DockerImage = "automaton-runner:latest",
            DockerNetwork = "automaton_runner_network",
            DataRoot = "/opt/automaton/hosted-runners/"
        };

        var command = HostedRunnerDockerCommandBuilder.BuildPreview(runner, options);

        Assert.Multiple(() =>
        {
            Assert.That(command, Does.Contain("--cpus \"0.5\""));
            Assert.That(command, Does.Contain("--memory \"768m\""));
            Assert.That(command, Does.Contain("AUTOMATON_SERVER_URL=\"https://api.automaton.monster\""));
            Assert.That(command, Does.Contain("AUTOMATON_API_KEY=\"*** generated runner key ***\""));
            Assert.That(command, Does.Contain("/opt/automaton/hosted-runners/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/data:/app/data"));
            Assert.That(command, Does.Not.Contain("ak_runner_"));
        });
    }
}
