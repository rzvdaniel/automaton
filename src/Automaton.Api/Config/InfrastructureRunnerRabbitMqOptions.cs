namespace Automaton.Api.Config;

public sealed class InfrastructureRunnerRabbitMqOptions
{
    public const string SectionName = "InfrastructureRunnerRabbitMq";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;
}
