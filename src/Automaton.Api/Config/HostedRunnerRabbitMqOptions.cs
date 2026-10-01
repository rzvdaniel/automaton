namespace Automaton.Api.Config;

public sealed class HostedRunnerRabbitMqOptions
{
    public const string SectionName = "HostedRunnerRabbitMq";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;

    public bool TlsEnabled { get; set; } = true;

    public string? TlsServerName { get; set; }
}
