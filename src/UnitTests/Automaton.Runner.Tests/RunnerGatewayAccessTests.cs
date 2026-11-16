using NUnit.Framework;
using System.Net;
using Automaton.Runner.Console.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerGatewayAccessTests
{
    private static DefaultHttpContext Request(string remote, string? header = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (header is not null) context.Request.Headers[RunnerGatewayAccess.SecretHeader] = header;
        return context;
    }

    [Test]
    public void WithoutASecretTheGatewayStaysOpenAsBefore()
        => Assert.That(RunnerGatewayAccess.IsAllowed(Request("172.18.0.5"), null), Is.True);

    [TestCase("172.18.0.1", "right-secret-0123456789", true)]
    [TestCase("172.18.0.1", "wrong-secret-0123456789", false)]
    [TestCase("172.18.0.1", null, false)]
    [TestCase("127.0.0.1", null, true)]
    public void WithASecretOnlyTheProxyAndTheRunnersOwnTunnelMayUseIt(string remote, string? header, bool allowed)
        => Assert.That(RunnerGatewayAccess.IsAllowed(Request(remote, header), "right-secret-0123456789"), Is.EqualTo(allowed));
}
