using Automaton.Core.Logs;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Serilog;

namespace Automaton.Core.Tests.Logs;

[TestFixture, NonParallelizable]
public sealed class LogForwardingTests
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

    [TearDown]
    public void Reset() => LogForwarding.SetAddress(null);

    [Test]
    public void Without_an_admin_endpoint_the_process_can_be_switched_on_and_off()
    {
        using var logger = new LoggerConfiguration().AddOtlpIfConfigured(Config(null), "t").CreateLogger();

        Assert.That(LogForwarding.IsSwitchable, Is.True);
        Assert.DoesNotThrow(() =>
        {
            logger.Information("before");
            LogForwarding.SetAddress("http://127.0.0.1:1");
            logger.Information("during");
            LogForwarding.SetAddress("not an address");
            logger.Information("after");
        });
    }

    [TestCase("http://127.0.0.1:1")]
    [TestCase("nonsense")]
    public void An_admin_endpoint_wins_and_is_never_replaced(string value)
    {
        using var first = new LoggerConfiguration().AddOtlpIfConfigured(Config(null), "t").CreateLogger();
        Assert.That(LogForwarding.IsSwitchable, Is.True);

        using var logger = new LoggerConfiguration().AddOtlpIfConfigured(Config(value), "t").CreateLogger();

        Assert.That(LogForwarding.IsSwitchable, Is.False, "an app's address cannot replace what the admin set, even a wrong value");
    }
}
