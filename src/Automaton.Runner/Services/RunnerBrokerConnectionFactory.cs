using System.Net.Security;
using System.Security.Authentication;
using Automaton.Core.Models;
using RabbitMQ.Client;

namespace Automaton.Runner.Console.Services;

internal static class RunnerBrokerConnectionFactory
{
    public static ConnectionFactory Create(HostedRunnerBrokerConfiguration broker, bool requireTls = false)
    {
        if (requireTls && !broker.TlsEnabled)
            throw new InvalidOperationException("Runner execution broker connections require TLS.");
        return new ConnectionFactory
        {
            HostName = broker.Host, Port = broker.Port,
            UserName = broker.Username, Password = broker.Password, VirtualHost = broker.VirtualHost,
            AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true,
            Ssl = new SslOption
            {
                Enabled = broker.TlsEnabled,
                ServerName = string.IsNullOrWhiteSpace(broker.TlsServerName) ? broker.Host : broker.TlsServerName,
                Version = SslProtocols.None,
                AcceptablePolicyErrors = SslPolicyErrors.None,
                CheckCertificateRevocation = true
            }
        };
    }
}
