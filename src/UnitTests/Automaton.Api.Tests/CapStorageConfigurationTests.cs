using Automaton.Api.Data;
using Automaton.Api.Messaging;
using Automaton.Messaging;
using DotNetCore.CAP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class CapStorageConfigurationTests
{
    [Test]
    public void ApiUsesItsOwnCapStorageNamespace()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        services.AddCap(CapRegistration.ConfigureStorage);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MySqlOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.TableNamePrefix, Is.EqualTo(CapStorageTablePrefixes.Api));
            Assert.That(CapStorageTablePrefixes.Api, Is.Not.EqualTo(CapStorageTablePrefixes.Worker));
        });
    }
}
