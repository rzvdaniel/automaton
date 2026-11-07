using NUnit.Framework;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerRuntimeGatewayProxyTests
{
    [TestCase("app-1.apps.example.test", "app-1.apps.example.test")]
    [TestCase("app-1.apps.example.test:8443", "app-1.apps.example.test:8443")]
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("a.example, b.example", null)]
    public void ApplyPublicHost_SendsTheVisitorsHostToTheApp(string? forwardedHost, string? expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://172.19.0.1:32788/");

        RunnerRuntimeGatewayProxy.ApplyPublicHost(request, forwardedHost);

        Assert.That(request.Headers.Host, Is.EqualTo(expected));
    }
}
