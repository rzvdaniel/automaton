using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class GenericApplicationContractPersistenceTests
{
    [Test]
    public async Task WebAppContractCanBePersistedAndLoadedWithoutStackSpecificFields()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var contract = new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec
                {
                    Executable = "arbitrary-runtime",
                    Arguments = ["--listen", "4312"],
                    WorkingDirectory = "service",
                    Environment = new Dictionary<string, string> { ["MODE"] = "test" }
                },
                Port = 4312
            },
            Health = new ApplicationHealthSpec { Path = "/ready", Port = 4312 },
            Storage = new ApplicationStorageSpec { DataDirectory = "var/data" }
        };

        await using (var writeContext = CreateContext(databaseName))
        {
            writeContext.WebApps.Add(new WebApp
            {
                Id = Guid.NewGuid(),
                Name = "Generic app",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                ApplicationContractJson = ApplicationContractSerialization.Serialize(contract),
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            });
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(databaseName);
        var stored = await readContext.WebApps.SingleAsync();
        var loaded = ApplicationContractSerialization.Deserialize(stored.ApplicationContractJson);

        Assert.That(loaded.Runtime.Command!.Executable, Is.EqualTo("arbitrary-runtime"));
        Assert.That(loaded.Runtime.Command.Arguments, Is.EqualTo(new[] { "--listen", "4312" }));
        Assert.That(loaded.Runtime.Command.WorkingDirectory, Is.EqualTo("service"));
        Assert.That(loaded.Health.Path, Is.EqualTo("/ready"));
        Assert.That(loaded.Storage.DataDirectory, Is.EqualTo("var/data"));
    }

    [Test]
    public void HistoricalRuntimeContractWithoutImageRemainsReadableButIsNotDeployable()
    {
        var contract = ApplicationContractSerialization.Deserialize("""
            {
              "build": { "command": null },
              "runtime": { "command": { "executable": "server" }, "port": 8080 },
              "health": {},
              "storage": { "dataDirectory": "data", "mounts": [] }
            }
            """);

        Assert.That(contract.Runtime.Command!.Executable, Is.EqualTo("server"));
        Assert.That(ApplicationContractValidator.ValidateForDeployment(contract), Has.Some.Contains("runtime.image"));
        Assert.Throws<InvalidOperationException>(() => ApplicationContractValidator.EnsureDeployable(contract));
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}
