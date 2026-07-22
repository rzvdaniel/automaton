using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Config;
using Automaton.Worker.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class ManagedRunnerProvisionRequestedEventHandlerTests
{
    [Test]
    public async Task PublishesTrustedInfrastructureActionWithoutRunningDocker()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(), Name = "host-1", Provider = "test",
            InfrastructureRunnerId = Guid.NewGuid(),
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner
        };
        var runner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerHostId = host.Id, RunnerHost = host,
            Name = "managed-test", ContainerName = "automaton-managed-test",
            Status = HostedRunnerStatuses.Provisioning,
            SpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                Resources = new HostedRunnerResources
                {
                    CpuLimit = 1, MemoryLimitMb = 1024, MemoryReservationMb = 512,
                    PidsLimit = 256, DiskLimitMb = 4096
                }
            }
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id,
            Action = HostedRunnerDeploymentActions.Provision,
            Status = HostedRunnerDeploymentStatuses.Running
        };
        runner.Deployments.Add(deployment);
        db.RunnerHosts.Add(host);
        db.HostedRunners.Add(runner);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var handler = new ManagedRunnerProvisionRequestedEventHandler(
            db, publisher,
            new OptionsSnapshot(new HostedRunnerOptions
            {
                Enabled = true, DockerImage = "automaton/runner:test",
                DockerNetwork = "automaton-runtime", ServerUrl = "https://api.example.test"
            }),
            NullLogger<ManagedRunnerProvisionRequestedEventHandler>.Instance);

        await handler.HandleAsync(new ManagedRunnerProvisionRequestedEvent
        {
            HostedRunnerId = runner.Id, UserId = runner.UserId, RunnerHostId = host.Id,
            RunnerApiKey = "ak_runner_secret", CorrelationId = "correlation"
        });

        var request = publisher.Messages.OfType<InfrastructureActionExecutionRequestedEvent>().Single();
        Assert.That(request.ActionKey, Is.EqualTo("provision-hosted-runner"));
        Assert.That(request.OperationId, Is.EqualTo(deployment.Id));
        Assert.That(request.InputJson, Does.Not.Contain("ak_runner_secret"));
        Assert.That(request.Secrets["AUTOMATON_PROVISION_RUNNER_API_KEY"], Is.EqualTo("ak_runner_secret"));
        Assert.That(runner.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Running));
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class OptionsSnapshot(HostedRunnerOptions value) : IOptionsSnapshot<HostedRunnerOptions>
    {
        public HostedRunnerOptions Value => value;
        public HostedRunnerOptions Get(string? name) => value;
    }
}
