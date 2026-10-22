namespace Automaton.Core.Models;

public sealed record RunnerBrokerConfiguration(
    string Host,
    int Port,
    string VirtualHost,
    string Username,
    string Password,
    string QueueName,
    bool TlsEnabled = false,
    string? TlsServerName = null,
    string? ManagementQueueName = null);
