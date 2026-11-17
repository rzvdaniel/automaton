using Automaton.Core.Logs;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Serilog;

namespace Automaton.Core.Tests.Logs;

[TestFixture]
public sealed class OtlpLogShippingTests
{
    private static IConfiguration Config(string? endpoint) => new FakeConfiguration(endpoint);

    private sealed class FakeConfiguration(string? endpoint) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == OtlpLogShipping.EndpointKey ? endpoint : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not a url")]
    [TestCase("ftp://logs:9428")]
    [TestCase("http://user:pw@logs:9428")]
    public void Unset_or_invalid_endpoint_turns_shipping_off(string? value)
        => Assert.That(OtlpLogShipping.ResolveEndpoint(Config(value)), Is.Null);

        [TestCase("http://victorialogs:9428", "http://victorialogs:9428/insert/opentelemetry/v1/logs")]
    [TestCase("http://victorialogs:9428/", "http://victorialogs:9428/insert/opentelemetry/v1/logs")]
    [TestCase("https://logs.example/custom/v1/logs", "https://logs.example/custom/v1/logs")]
    public void Bare_address_gets_the_victorialogs_path(string value, string expected)
        => Assert.That(OtlpLogShipping.ResolveEndpoint(Config(value)), Is.EqualTo(expected));

    [Test]
    public void Logger_builds_with_and_without_endpoint()
    {
        using var off = new LoggerConfiguration().AddOtlpIfConfigured(Config(null), "t").CreateLogger();
        using var on = new LoggerConfiguration().AddOtlpIfConfigured(Config("http://127.0.0.1:1"), "t").CreateLogger();
        Assert.DoesNotThrow(() => on.Information("hello"));
    }
}
