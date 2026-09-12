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
        Assert.That(CountOccurrences(cloudInit, "-v /var/run/docker.sock:/var/run/docker.sock"), Is.EqualTo(1));
        Assert.That(CountOccurrences(cloudInit, "-v /:/host"), Is.EqualTo(1));
        Assert.That(cloudInit, Does.Contain("--network host"));
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

    [Test]
    public void Generate_InjectsPrivilegedRunnerRabbitMqConfigurationIntoProtectedEnvFile()
    {
        var cloudInit = RunnerHostCloudInit.Generate(new RunnerHostCloudInitRequest(
            new Uri("https://api.example.test"), Guid.NewGuid(), "re_test-token", "runner:latest",
            RabbitMq: new RunnerRabbitMqConfiguration("rabbitmq.internal", 5672, "infra", "secret", "/", "cap.default.router")));

        Assert.Multiple(() =>
        {
            Assert.That(cloudInit, Does.Contain("RabbitMq__Host=rabbitmq.internal"));
            Assert.That(cloudInit, Does.Contain("RabbitMq__Port=5672"));
            Assert.That(cloudInit, Does.Contain("RabbitMq__Username=infra"));
            Assert.That(cloudInit, Does.Contain("RabbitMq__Password=secret"));
            Assert.That(cloudInit, Does.Contain("RabbitMq__VirtualHost=/"));
            Assert.That(cloudInit, Does.Contain("RabbitMq__ExchangeName=cap.default.router"));
            Assert.That(cloudInit, Does.Contain("permissions: \"0600\""));
            Assert.That(cloudInit, Does.Contain("shred -u /etc/automaton/infrastructure-runner.env"));
        });
    }

    [Test]
    public void ManagedRunnerInstallDoesNotReceiveInfrastructureRabbitMqConfiguration()
    {
        var managedInstall = "AUTOMATON_RUNNER_PURPOSE=Managed\nAUTOMATON_SERVER_URL=https://api.example.test";
        Assert.That(managedInstall, Does.Not.Contain("RabbitMq__Password"));
    }

    private static int CountOccurrences(string value, string fragment)
        => value.Split(fragment, StringSplitOptions.None).Length - 1;
}
