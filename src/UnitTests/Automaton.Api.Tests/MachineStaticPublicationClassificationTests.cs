using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class MachineStaticPublicationClassificationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PublishIfAssignedAsync_SkipsDynamicAndComposeApps(bool compose)
    {
        var contract = compose
            ? new ApplicationContract
            {
                Compose = new ApplicationComposeSpec
                {
                    PublicService = "web",
                    Services = [new ApplicationComposeService { Name = "web", Image = "nginx:alpine", Port = 8080 }]
                }
            }
            : new ApplicationContract { Runtime = { Command = new ApplicationCommandSpec { Executable = "serve" } } };
        await using var fixture = await PublicationFixture.CreateAsync(contract);

        var published = await fixture.Service.PublishIfAssignedAsync(
            fixture.App, AppRuntimeVisibilities.Public, Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.False);
            Assert.That(fixture.Transfers.ExecuteCalls, Is.Zero);
        });
    }

    [Test]
    public async Task PublishIfAssignedAsync_QueuesOnlyDeclaredStaticOutput()
    {
        var contract = new ApplicationContract
        {
            Build = new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec { Executable = "build" },
                OutputDirectory = "dist"
            }
        };
        await using var fixture = await PublicationFixture.CreateAsync(contract);

        var published = await fixture.Service.PublishIfAssignedAsync(
            fixture.App, AppRuntimeVisibilities.Public, Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(fixture.Transfers.ExecuteCalls, Is.EqualTo(1));
            Assert.That(fixture.Transfers.Command?.Kind, Is.EqualTo(RunnerFileTransferKinds.PublishStaticSite));
            Assert.That(fixture.Transfers.Command?.RelativePath, Is.EqualTo("dist"));
        });
    }

    private sealed class PublicationFixture(
        ApplicationDbContext db,
        WebApp app,
        MachineStaticAppPublicationService service,
        CapturingTransferGateway transfers) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public WebApp App { get; } = app;
        public MachineStaticAppPublicationService Service { get; } = service;
        public CapturingTransferGateway Transfers { get; } = transfers;
        public ValueTask DisposeAsync() => Db.DisposeAsync();

        public static async Task<PublicationFixture> CreateAsync(ApplicationContract contract)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var runnerId = Guid.NewGuid();
            var app = new WebApp
            {
                Id = Guid.NewGuid(),
                Name = "static-classification-test",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                ApplicationContractJson = ApplicationContractSerialization.Serialize(contract),
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };
            db.Runners.Add(new Runner
            {
                Id = runnerId,
                Name = "machine runner",
                Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.MachineManagement
            });
            app.DeploymentTargets.Add(AppDeploymentTarget.CreateDefault(app.Id, runnerId));
            db.WebApps.Add(app);
            await db.SaveChangesAsync();
            var transfers = new CapturingTransferGateway();
            return new PublicationFixture(db, app, new MachineStaticAppPublicationService(db, transfers), transfers);
        }
    }

    private sealed class CapturingTransferGateway : IRunnerFileTransferGateway
    {
        public int ExecuteCalls { get; private set; }
        public RunnerFileTransferCommand? Command { get; private set; }

        public Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(
            Guid operationId, Guid runnerId, Guid ownerId, RunnerFileTransferCommand command,
            IFormFile? input, CancellationToken token, TimeSpan? timeout = null)
        {
            ExecuteCalls++;
            Command = command;
            return Task.FromResult((operationId, new RunnerExecutionResult
            {
                Outcome = "Succeeded",
                FileTransferResult = new RunnerFileTransferResult { Success = true }
            }));
        }

        public Task QueueAsync(Guid operationId, Guid runnerId, Guid ownerId,
            RunnerFileTransferCommand command, CancellationToken token) => Task.CompletedTask;
        public Stream OpenOutput(Guid operationId) => throw new NotSupportedException();
        public Task CleanupOutputAsync(Guid operationId) => Task.CompletedTask;
    }
}
