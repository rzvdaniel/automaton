using Automaton.Messaging;
using Automaton.Worker.Messaging;
using DotNetCore.CAP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class CapStorageConfigurationTests
{
    [Test]
    public void WorkerUsesItsOwnCapStorageNamespace()
    {
        var services = new ServiceCollection();
        services.AddCap(options => CapRegistration.ConfigureStorage(options, "Server=localhost;Database=automaton;"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MySqlOptions>>().Value;

        Assert.That(options.TableNamePrefix, Is.EqualTo(CapStorageTablePrefixes.Worker));
    }
}
