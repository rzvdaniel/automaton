using System.IO.Compression;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class DeployPackageContractServiceTests
{
    [Test]
    public async Task PackageDeployPersistenceRoundTripsContractThroughApiAndPullRepresentations()
    {
        var appId = Guid.NewGuid();
        var contract = CreateContract();
        var deployedContract = await ReadContractFromPackageAsync(appId, contract);
        var pulledContract = await PersistAndReadPulledContractAsync(appId, deployedContract);

        Assert.Multiple(() =>
        {
            Assert.That(pulledContract.Build.Command!.Executable, Is.EqualTo("build-tool"));
            Assert.That(pulledContract.Build.Command.Arguments, Is.EqualTo(new[] { "--target", "release" }));
            Assert.That(pulledContract.Runtime.Command!.WorkingDirectory, Is.EqualTo("app"));
            Assert.That(pulledContract.Runtime.Port, Is.EqualTo(5000));
            Assert.That(pulledContract.Health.Path, Is.Null);
            Assert.That(pulledContract.Storage.DataDirectory, Is.EqualTo("var/data"));
        });
    }

    private static ApplicationContract CreateContract() => new()
    {
        Build = new ApplicationBuildSpec
        {
            Command = new ApplicationCommandSpec
            {
                Executable = "build-tool",
                Arguments = ["--target", "release"],
                WorkingDirectory = "tools",
                Environment = new Dictionary<string, string> { ["MODE"] = "production" }
            },
            OutputDirectory = "artifacts"
        },
        Runtime = new ApplicationRuntimeSpec
        {
            Image = "test/runtime:1",
            Command = new ApplicationCommandSpec
            {
                Executable = "arbitrary-server",
                Arguments = ["--port", "5000"],
                WorkingDirectory = "app"
            },
            Port = 5000
        },
        Health = new ApplicationHealthSpec { Path = null, Port = 5000 },
        Storage = new ApplicationStorageSpec { DataDirectory = "var/data" }
    };

    private static async Task<ApplicationContract> ReadContractFromPackageAsync(
        Guid appId,
        ApplicationContract contract)
    {
        await using var packageStream = CreatePackage(appId, contract);
        var packageFile = new FormFile(packageStream, 0, packageStream.Length, "file", "deploy-package.zip");
        return await DeployPackageContractService.ReadAsync(packageFile, appId, CancellationToken.None);
    }

    private static async Task<ApplicationContract> PersistAndReadPulledContractAsync(
        Guid appId,
        ApplicationContract deployedContract)
    {
        await using var dbContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Round trip",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}"
        });
        await dbContext.SaveChangesAsync();
        await DeployPackageContractService.PersistAsync(dbContext, appId, deployedContract, CancellationToken.None);

        var apiContractJson = await dbContext.WebApps
            .Where(app => app.Id == appId)
            .Select(app => app.ApplicationContractJson)
            .SingleAsync();
        var apiContract = ApplicationContractSerialization.Deserialize(apiContractJson);
        var pulledContractJson = ApplicationContractSerialization.Serialize(apiContract);
        return ApplicationContractSerialization.Deserialize(pulledContractJson);
    }

    [Test]
    public void ReadAsyncRejectsPackageForAnotherApplication()
    {
        var packageStream = CreatePackage(Guid.NewGuid(), ApplicationContractDefaults.Create());
        var packageFile = new FormFile(packageStream, 0, packageStream.Length, "file", "deploy-package.zip");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeployPackageContractService.ReadAsync(packageFile, Guid.NewGuid(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("does not match"));
    }

    private static MemoryStream CreatePackage(Guid appId, ApplicationContract contract)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(JsonSerializer.Serialize(new
            {
                appId,
                application = contract
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        stream.Position = 0;
        return stream;
    }
}
