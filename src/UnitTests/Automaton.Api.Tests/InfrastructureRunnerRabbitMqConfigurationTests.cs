using Automaton.Api.Config;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public class InfrastructureRunnerRabbitMqConfigurationTests
{
    [Test]
    public void TryBuildEnvironment_UsesCanonicalRabbitMqSettings()
    {
        var options = new RabbitMqOptions
        {
            Host = "rabbitmq.internal",
            Port = 5672,
            Username = "infra",
            Password = "secret",
            VirtualHost = "/",
            ExchangeName = "cap.default.router"
        };

        Assert.That(InfrastructureRunnerRabbitMqConfigurationService.TryBuildEnvironment(options, out var environment), Is.True);
        Assert.That(environment, Does.Contain("RabbitMq__Host=rabbitmq.internal"));
        Assert.That(environment, Does.Contain("RabbitMq__Port=5672"));
        Assert.That(environment, Does.Contain("RabbitMq__Username=infra"));
        Assert.That(environment, Does.Contain("RabbitMq__Password=secret"));
        Assert.That(environment, Does.Contain("RabbitMq__VirtualHost=/"));
        Assert.That(environment, Does.Contain("RabbitMq__ExchangeName=cap.default.router"));
    }

    [TestCase("")]
    [TestCase("line\nbreak")]
    public void TryBuildEnvironment_FailsClosedForInvalidBrokerSettings(string host)
    {
        var options = new RabbitMqOptions
        {
            Host = host,
            Port = 5672,
            Username = "infra",
            Password = "secret"
        };

        Assert.That(InfrastructureRunnerRabbitMqConfigurationService.TryBuildEnvironment(options, out _), Is.False);
    }
}
