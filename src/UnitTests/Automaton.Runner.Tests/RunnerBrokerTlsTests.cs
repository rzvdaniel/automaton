using System.Net.Security;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Automaton.UnitTests.Shared;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerBrokerTlsTests
{
    [Test]
    public void ExecutionRequiresExplicitTlsAndValidatesTheConfiguredHostname()
    {
        var broker = Configuration(5671) with { TlsEnabled = false };
        Assert.Throws<InvalidOperationException>(() => RunnerBrokerConnectionFactory.Create(broker, requireTls: true));
        var factory = RunnerBrokerConnectionFactory.Create(broker with { TlsEnabled = true }, requireTls: true);
        Assert.That(factory.Ssl.Enabled, Is.True);
        Assert.That(factory.Ssl.ServerName, Is.EqualTo("localhost"));
        Assert.That(factory.Ssl.AcceptablePolicyErrors, Is.EqualTo(SslPolicyErrors.None));
        Assert.That(factory.Ssl.CertificateValidationCallback, Is.Null);
        Assert.That(factory.Ssl.CheckCertificateRevocation, Is.True);
        var named = RunnerBrokerConnectionFactory.Create(broker with { TlsEnabled = true, TlsServerName = "broker.example" });
        Assert.That(named.Ssl.ServerName, Is.EqualTo("broker.example"));
    }

    [Test]
    public async Task RabbitClientRejectsUntrustedCertificateBeforeSendingAmqpData()
    {
        await using var server = new LoopbackTlsServer();
        var factory = Factory(server.Port);
        Assert.ThrowsAsync<BrokerUnreachableException>(async () =>
        {
            await using var connection = await factory.CreateConnectionAsync();
        });
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(server.AcceptedConnection, Is.True);
        Assert.That(server.ReceivedApplicationData, Is.False);
    }

    [TestCase("localhost", true)]
    [TestCase("wrong.example", false)]
    public async Task RabbitClientNegotiatesTlsWithTestLocalTrustAndRejectsHostnameMismatch(string serverName, bool accepted)
    {
        await using var server = new LoopbackTlsServer();
        var factory = Factory(server.Port);
        factory.Ssl.ServerName = serverName;
        // Test-local trust for this disposable certificate only; production uses RabbitMQ's strict default validator.
        // This avoids changing the operating system trust store or requiring an external broker.
        factory.Ssl.CertificateValidationCallback = (_, certificate, _, errors) =>
            certificate?.GetCertHashString() == server.CertificateThumbprint
            && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None;
        Assert.ThrowsAsync<BrokerUnreachableException>(async () =>
        {
            await using var connection = await factory.CreateConnectionAsync();
        });
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(server.ReceivedApplicationData, Is.EqualTo(accepted),
            "The loopback listener speaks TLS, not AMQP: accepted TLS must reach the AMQP header; invalid TLS must not.");
    }

    private static ConnectionFactory Factory(int port)
    {
        var factory = RunnerBrokerConnectionFactory.Create(Configuration(port), requireTls: true);
        factory.AutomaticRecoveryEnabled = false;
        factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(3);
        factory.HandshakeContinuationTimeout = TimeSpan.FromSeconds(3);
        return factory;
    }

    private static HostedRunnerBrokerConfiguration Configuration(int port)
        => new("localhost", port, "/", "test", "test", "test", TlsEnabled: true);
}
