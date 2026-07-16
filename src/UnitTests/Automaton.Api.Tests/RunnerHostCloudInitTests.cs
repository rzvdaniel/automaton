using Automaton.Infrastructure.RunnerHosts;

namespace Automaton.Api.Tests;

public class RunnerHostCloudInitTests
{
    [Test]
    public void Generate_UsesExpectedInfrastructureRunnerMountsAndSingleUseEnrollment()
    {
        var hostId = Guid.NewGuid();
        var cloudInit = RunnerHostCloudInit.Generate(new RunnerHostCloudInitRequest(
            new Uri("https://api.example.test"),
            hostId,
            "re_test-token",
            "ghcr.io/example/automaton-runner:latest"));

        Assert.That(cloudInit, Does.StartWith("#cloud-config"));
        Assert.That(cloudInit, Does.Contain("AUTOMATON_RUNNER_PURPOSE=Infrastructure"));
        Assert.That(cloudInit, Does.Contain($"AUTOMATON_RUNNER_HOST_ID={hostId}"));
        Assert.That(cloudInit, Does.Contain("-v '/opt/automaton/infrastructure-runner/config':/app/config"));
        Assert.That(cloudInit, Does.Contain("-v '/opt/automaton/infrastructure-runner/workspace':/automaton/workspace"));
        Assert.That(cloudInit, Does.Contain("-v '/opt/automaton/infrastructure-runner/artifacts':/automaton/artifacts"));
        Assert.That(cloudInit, Does.Contain("-v '/opt/automaton/infrastructure-runner/logs':/app/logs"));
        Assert.That(cloudInit, Does.Contain("shred -u /etc/automaton/infrastructure-runner.env"));
        Assert.That(cloudInit, Does.Not.Contain("AUTOMATON_API_KEY"));
    }

    [Test]
    public void Generate_RejectsMissingEnrollmentToken()
    {
        Assert.That(
            () => RunnerHostCloudInit.Generate(new RunnerHostCloudInitRequest(
                new Uri("https://api.example.test"),
                Guid.NewGuid(),
                string.Empty,
                "automaton-runner:latest")),
            Throws.ArgumentException);
    }
}
