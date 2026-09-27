using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Config;
using Automaton.Worker.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Worker.Tests;

internal static class ManagedRunnerProvisionScenarios
{
    internal const string StorageFilesystemUuid = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

    internal static async Task VerifyTrustedProvisionLifecycleAsync()
    {
        await using var fixture = await ManagedRunnerProvisionFixture.CreateAsync();
        var grantId = Guid.NewGuid();
        var request = fixture.CreateRequest(grantId: grantId);
        await fixture.AssertOutboxFailureRollsBackAsync(request);
        await fixture.HandleAsync(request, true, Image('a'));
        AssertProvisionCommand(fixture, request, grantId);
        await fixture.AssertDuplicateIsIgnoredAsync(request);
        await VerifyDisabledProvisioningAsync(fixture);
        await VerifyReprovisionAsync(fixture);
    }

    internal static async Task VerifyInvalidImageFailsClosedAsync()
    {
        await using var fixture = await ManagedRunnerProvisionFixture.CreateAsync();
        await fixture.HandleAsync(fixture.CreateRequest(), true, "automaton/runner:latest");
        await fixture.AssertInvalidImageFailureAsync();
    }

    internal static async Task VerifyUnknownFilesystemUuidIsOmittedAsync()
    {
        await using var fixture = await ManagedRunnerProvisionFixture.CreateAsync();
        fixture.ClearStorageFilesystemUuid();
        await fixture.HandleAsync(fixture.CreateRequest(grantId: Guid.NewGuid()), true, Image('a'));

        using var input = JsonDocument.Parse(fixture.SingleCommand().InputJson);
        Assert.Multiple(() =>
        {
            Assert.That(input.RootElement.TryGetProperty("storageFilesystemUuid", out _), Is.False);
            Assert.That(input.RootElement.GetProperty("xfsProjectId").GetInt32(), Is.EqualTo(1234567));
        });
    }

    internal static async Task VerifyConflictingStorageHistoryFailsClosedAsync()
    {
        await using var fixture = await ManagedRunnerProvisionFixture.CreateAsync();
        var otherHost = new RunnerHost
        {
            Id = Guid.NewGuid(), Name = "host-2", Provider = "test",
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner
        };
        fixture.AddConflictingStorageRunner(otherHost);
        await fixture.HandleAsync(fixture.CreateRequest(grantId: Guid.NewGuid()), true, Image('a'));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.CommandCount, Is.Zero);
            Assert.That(fixture.Deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(fixture.Deployment.Error, Does.Contain("multiple Runner Hosts"));
            Assert.That(fixture.StorageRunnerHostId, Is.Null);
        });
    }

    private static string Image(char digestCharacter)
        => "automaton/runner@sha256:" + new string(digestCharacter, 64);

    private static void AssertProvisionCommand(
        ManagedRunnerProvisionFixture fixture,
        ManagedRunnerProvisionRequestedEvent request,
        Guid grantId)
    {
        var command = fixture.SingleCommand();
        Assert.Multiple(() =>
        {
            Assert.That(command.ActionKey, Is.EqualTo("provision-hosted-runner"));
            Assert.That(command.CommandId, Is.EqualTo(fixture.Deployment.Id));
            Assert.That(command.OperationId, Is.EqualTo(fixture.Deployment.Id));
            Assert.That(command.RunnerHostId, Is.EqualTo(fixture.Host.Id));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(fixture.Host.InfrastructureRunnerId));
            Assert.That(command.SecretGrantIds, Is.EqualTo(new[] { grantId }));
            Assert.That(JsonSerializer.Serialize(request), Does.Not.Contain("ak_runner_"));
            Assert.That(JsonSerializer.Serialize(command), Does.Not.Contain("ak_runner_"));
            Assert.That(JsonSerializer.Serialize(command), Does.Not.Contain("EnrollmentToken"));
        });
        AssertProvisionInput(command.InputJson, fixture.Runner.Id);
        Assert.That(fixture.Runner.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(fixture.Deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Dispatching));
    }

    private static void AssertProvisionInput(string inputJson, Guid runnerId)
    {
        using var input = JsonDocument.Parse(inputJson);
        var root = input.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("runnerPort").GetInt32(), Is.EqualTo(8080));
            Assert.That(root.GetProperty("runtimeHostAddress").GetString(), Is.EqualTo("10.20.30.40"));
            Assert.That(root.GetProperty("cpuLimit").GetDecimal(), Is.EqualTo(1));
            Assert.That(root.GetProperty("memoryMb").GetInt32(), Is.EqualTo(1024));
            Assert.That(root.GetProperty("diskLimitMb").GetInt32(), Is.EqualTo(4096));
            Assert.That(root.GetProperty("writableLayerSizeLimitMb").GetInt32(), Is.EqualTo(512));
            Assert.That(root.GetProperty("xfsProjectId").GetInt32(), Is.EqualTo(1234567));
            Assert.That(root.GetProperty("storageFilesystemUuid").GetString(), Is.EqualTo(StorageFilesystemUuid));
            Assert.That(root.GetProperty("pidsLimit").GetInt32(), Is.EqualTo(256));
            Assert.That(root.GetProperty("sharedMemoryMb").GetInt32(), Is.EqualTo(96));
            Assert.That(root.GetProperty("dockerLogMaxSizeMb").GetInt32(), Is.EqualTo(20));
            Assert.That(root.GetProperty("dockerLogMaxFiles").GetInt32(), Is.EqualTo(4));
            Assert.That(root.GetProperty("systemVolumeName").GetString(), Is.EqualTo($"automaton-hosted-runner-{runnerId:N}-system"));
            Assert.That(root.GetProperty("userVolumeName").GetString(), Is.EqualTo($"automaton-hosted-runner-{runnerId:N}-user"));
        });
    }

    private static async Task VerifyDisabledProvisioningAsync(ManagedRunnerProvisionFixture fixture)
    {
        var disabled = fixture.AddDeployment(HostedRunnerDeploymentActions.Reprovision);
        await fixture.HandleAsync(fixture.CreateRequest(disabled), false, string.Empty);
        Assert.That(disabled.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
        Assert.That(fixture.CommandCount, Is.EqualTo(1));
    }

    private static async Task VerifyReprovisionAsync(ManagedRunnerProvisionFixture fixture)
    {
        var deployment = fixture.Deployments.Last();
        await fixture.HandleAsync(fixture.CreateRequest(deployment, Guid.NewGuid()), true, Image('b'));
        var command = fixture.CommandFor(deployment.Id);
        Assert.Multiple(() =>
        {
            Assert.That(command.ActionKey, Is.EqualTo("reprovision-hosted-runner"));
            Assert.That(command.CommandId, Is.EqualTo(command.OperationId));
        });
    }
}

internal sealed class ManagedRunnerProvisionFixture : IAsyncDisposable
{
    private readonly RecordingPublisher publisher = new();
    private readonly ApplicationDbContext db;
    internal RunnerHost Host { get; }
    internal HostedRunner Runner { get; }
    internal HostedRunnerDeployment Deployment { get; }
    internal List<HostedRunnerDeployment> Deployments { get; } = [];
    internal int CommandCount => publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Count();
    internal Guid? StorageRunnerHostId => db.AccountStorageAllocations.Single().StorageRunnerHostId;

    private ManagedRunnerProvisionFixture(
        ApplicationDbContext db,
        RunnerHost host,
        HostedRunner runner,
        HostedRunnerDeployment deployment)
    {
        this.db = db;
        Host = host;
        Runner = runner;
        Deployment = deployment;
        Deployments.Add(deployment);
    }

    internal static async Task<ManagedRunnerProvisionFixture> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var host = CreateHost();
        var runner = CreateRunner(host);
        var deployment = CreateDeployment(runner, HostedRunnerDeploymentActions.Provision);
        db.RunnerHosts.Add(host);
        db.HostedRunners.Add(runner);
        db.HostedRunnerDeployments.Add(deployment);
        db.AccountStorageAllocations.Add(new AccountStorageAllocation
        {
            AccountId = runner.UserId,
            StorageProjectId = runner.StorageProjectId!.Value,
            StorageRunnerHostId = host.Id,
            StorageFilesystemUuid = ManagedRunnerProvisionScenarios.StorageFilesystemUuid
        });
        await db.SaveChangesAsync();
        return new ManagedRunnerProvisionFixture(db, host, runner, deployment);
    }

    private static RunnerHost CreateHost()
        => new()
        {
            Id = Guid.NewGuid(), Name = "host-1", Provider = "test", InfrastructureRunnerId = Guid.NewGuid(),
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner, PrivateIp = "10.20.30.40"
        };

    private static HostedRunner CreateRunner(RunnerHost host)
        => new()
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerHostId = host.Id, RunnerHost = host,
            StorageProjectId = 1234567, Name = "managed-test", ContainerName = "automaton-managed-test",
            Status = HostedRunnerStatuses.Provisioning, HostedRunnerSpecificationId = Guid.NewGuid(),
            SpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                Resources = new HostedRunnerResources
                {
                    CpuLimit = 1, MemoryLimitMb = 1024, MemoryReservationMb = 512, PidsLimit = 256,
                    DiskLimitMb = 4096, SharedMemoryMb = 96, DockerLogMaxSizeMb = 20, DockerLogMaxFiles = 4
                }
            }
        };

    private static HostedRunnerDeployment CreateDeployment(HostedRunner runner, string action)
        => new()
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id,
            Action = action, Status = HostedRunnerDeploymentStatuses.Queued
        };

    internal HostedRunnerDeployment AddDeployment(string action)
    {
        var deployment = CreateDeployment(Runner, action);
        Runner.Deployments.Add(deployment);
        db.HostedRunnerDeployments.Add(deployment);
        db.SaveChanges();
        Deployments.Add(deployment);
        return deployment;
    }

    internal void AddConflictingStorageRunner(RunnerHost otherHost)
    {
        db.RunnerHosts.Add(otherHost);
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Runner.UserId, RunnerHostId = otherHost.Id, RunnerHost = otherHost,
            StorageProjectId = Runner.StorageProjectId, ContainerId = "legacy-container",
            Name = "legacy-runner", ContainerName = "legacy-runner", Status = HostedRunnerStatuses.Running
        });
        db.AccountStorageAllocations.Single().StorageRunnerHostId = null;
        db.SaveChanges();
    }

    internal ManagedRunnerProvisionRequestedEvent CreateRequest(
        HostedRunnerDeployment? deployment = null,
        Guid? grantId = null)
        => new()
        {
            OperationId = (deployment ?? Deployment).Id, HostedRunnerId = Runner.Id, UserId = Runner.UserId,
            RunnerHostId = Host.Id, SecretGrantIds = grantId.HasValue ? [grantId.Value] : [], CorrelationId = "correlation"
        };

    internal Task HandleAsync(ManagedRunnerProvisionRequestedEvent request, bool enabled, string image)
        => ManagedRunnerProvisionHandlerFactory.Create(
            db, publisher, new RollbackTransactionalEventExecutor(db), enabled, image).HandleAsync(request);

    internal Task AssertOutboxFailureRollsBackAsync(ManagedRunnerProvisionRequestedEvent request)
    {
        publisher.FailNext = true;
        Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(request, true, "automaton/runner@sha256:" + new string('a', 64)));
        Assert.That(Deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
        return Task.CompletedTask;
    }

    internal async Task AssertDuplicateIsIgnoredAsync(ManagedRunnerProvisionRequestedEvent request)
    {
        await HandleAsync(request, true, "automaton/runner@sha256:" + new string('a', 64));
        Assert.That(CommandCount, Is.EqualTo(1));
    }

    internal async Task AssertInvalidImageFailureAsync()
    {
        db.ChangeTracker.Clear();
        var runner = await db.HostedRunners.SingleAsync();
        var deployment = await db.HostedRunnerDeployments.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(runner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
            Assert.That(runner.LastError, Does.Contain("immutable"));
            Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(deployment.Error, Does.Contain("immutable"));
            Assert.That(publisher.Messages.OfType<InfrastructureCommandRequestedEvent>(), Is.Empty);
        });
    }

    internal InfrastructureCommandRequestedEvent SingleCommand()
        => publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();

    internal void ClearStorageFilesystemUuid()
        => db.AccountStorageAllocations.Single().StorageFilesystemUuid = null;

    internal InfrastructureCommandRequestedEvent CommandFor(Guid operationId)
        => publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single(x => x.OperationId == operationId);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private sealed class RecordingPublisher : IEventPublisher
    {
        internal List<object> Messages { get; } = [];
        internal bool FailNext { get; set; }
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Simulated outbox write failure.");
            }
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RollbackTransactionalEventExecutor(ApplicationDbContext db) : ITransactionalEventExecutor
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            var snapshots = db.ChangeTracker.Entries().ToDictionary(entry => entry, entry => entry.CurrentValues.Clone());
            try
            {
                await operation(cancellationToken);
            }
            catch
            {
                foreach (var (entry, values) in snapshots)
                {
                    entry.CurrentValues.SetValues(values);
                    entry.State = EntityState.Modified;
                }
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
    }

}

internal static class ManagedRunnerProvisionHandlerFactory
{
    internal static ManagedRunnerProvisionRequestedEventHandler Create(
        ApplicationDbContext db,
        IEventPublisher publisher,
        ITransactionalEventExecutor transactionalEventExecutor,
        bool enabled,
        string image)
        => new(db,
            new HostedRunnerStorageLocalityAdoptionService(db, new HostedRunnerStorageLocalityResolver(db)),
            publisher,
            transactionalEventExecutor,
            new HostedRunnerOptionsSnapshot(new HostedRunnerOptions
            {
                Enabled = enabled, DockerImage = image, DockerNetwork = "automaton-runtime",
                ServerUrl = "https://api.example.test"
            }), NullLogger<ManagedRunnerProvisionRequestedEventHandler>.Instance);

    private sealed class HostedRunnerOptionsSnapshot(HostedRunnerOptions value) : IOptionsSnapshot<HostedRunnerOptions>
    {
        public HostedRunnerOptions Value => value;
        public HostedRunnerOptions Get(string? name) => value;
    }
}
