using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Services;

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
            Assert.That(command, Does.Contain("mkdir -p '/opt/automaton/hosted-runners/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/config'"));
            Assert.That(command, Does.Contain("--cpus '0.5'"));
            Assert.That(command, Does.Contain("--memory '768m'"));
            Assert.That(command, Does.Contain("AUTOMATON_SERVER_URL='https://api.automaton.monster'"));
            Assert.That(command, Does.Contain("AUTOMATON_API_KEY='*** generated runner key ***'"));
            Assert.That(command, Does.Contain("docker volume create 'automaton-runner-aaaaaaaabbbbccccddddeeeeeeeeeeee-data'"));
            Assert.That(command, Does.Contain("-v 'automaton-runner-aaaaaaaabbbbccccddddeeeeeeeeeeee-data:/app/data'"));
            Assert.That(command, Does.Not.Contain("/opt/automaton/hosted-runners/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/data:/app/data"));
            Assert.That(command, Does.Not.Contain("ak_runner_"));
        });
    }

    [Test]
    public void BuildProvisionScript_IncludesRawApiKeyOnlyForExecution()
    {
        var runner = new HostedRunner
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name = "Hosted Runner abc123",
            ContainerName = "automaton-hosted-runner-abc123",
            CpuCores = 1m,
            MemoryMb = 1024
        };

        var command = HostedRunnerDockerCommandBuilder.BuildProvisionScript(
            runner,
            new HostedRunnerOptions(),
            "ak_runner_secret_for_remote_execution");

        Assert.Multiple(() =>
        {
            Assert.That(command, Does.Contain("docker rm -f 'automaton-hosted-runner-abc123'"));
            Assert.That(command, Does.Contain("docker volume create 'automaton-runner-aaaaaaaabbbbccccddddeeeeeeeeeeee-data'"));
            Assert.That(command, Does.Contain("AUTOMATON_API_KEY='ak_runner_secret_for_remote_execution'"));
        });
    }

    [Test]
    public void BuildProvisionScript_UsesStableHostedRunnerDataVolumeAndDoesNotDeleteIt()
    {
        var runner = new HostedRunner
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name = "Hosted Runner abc123",
            ContainerName = "automaton-hosted-runner-recreated",
            ContainerId = "temporary-container-id"
        };

        var command = HostedRunnerDockerCommandBuilder.BuildProvisionScript(
            runner,
            new HostedRunnerOptions(),
            "ak_runner_secret_for_remote_execution");

        Assert.Multiple(() =>
        {
            Assert.That(command, Does.Contain("automaton-runner-aaaaaaaabbbbccccddddeeeeeeeeeeee-data:/app/data"));
            Assert.That(command, Does.Not.Contain("temporary-container-id"));
            Assert.That(command, Does.Not.Contain("docker volume rm"));
            Assert.That(command, Does.Not.Contain("docker rm -v"));
        });
    }
}
