using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostContainerExecutionOperationTests
{
    [Test]
    public async Task ResultIsDurableIdempotentAndNotVisibleAcrossRunnerAccounts()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var queued = await fixture.QueueAsync();
        var outcome = await fixture.ApplyAndReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(queued.Completed, Is.False);
            Assert.That(outcome.FirstApply, Is.EqualTo("accepted"));
            Assert.That(outcome.ReplayApply, Is.EqualTo("already-applied"));
            Assert.That(outcome.Retried.Completed, Is.True);
            Assert.That(outcome.Retried.Result?.Success, Is.True);
            Assert.That(outcome.Visible?.Result?.ResolvedImage, Is.EqualTo("example/image@sha256:abc"));
            Assert.That(outcome.OtherAccount, Is.Null);
            Assert.That(outcome.PublishedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RetryCannotMovePersistedOperationToAnotherInfrastructureRunner()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        await fixture.QueueAsync();

        var retry = await fixture.QueueOnDifferentInfrastructureRunnerAsync();

        Assert.Multiple(() =>
        {
            Assert.That(retry.Completed, Is.True);
            Assert.That(retry.Result?.ErrorCode, Is.EqualTo("operation_conflict"));
            Assert.That(fixture.PublishedCount, Is.EqualTo(1));
        });
    }

    private sealed class OperationFixture : IAsyncDisposable
    {
        private readonly ApplicationDbContext db;
        private readonly HostContainerExecutionOperationService service;
        private readonly RecordingPublisher events;
        private readonly HostContainerExecutionRequest request;
        private readonly Guid hostedKey;
        private readonly Guid infrastructureKey;
        private readonly Guid infrastructureId;

        private OperationFixture(ApplicationDbContext db,
            HostContainerExecutionOperationService service, RecordingPublisher events,
            HostContainerExecutionRequest request, Guid hostedKey, Guid infrastructureKey, Guid infrastructureId)
            => (this.db, this.service, this.events, this.request, this.hostedKey, this.infrastructureKey, this.infrastructureId)
                = (db, service, events, request, hostedKey, infrastructureKey, infrastructureId);

        public int PublishedCount => events.Events.Count;

        public static async Task<OperationFixture> CreateAsync()
        {
            var db = CreateDatabase();
            var (hostedKey, infraKey, infraId, hostedRunnerId) = await SeedRunnersAsync(db);
            var events = new RecordingPublisher();
            var service = new HostContainerExecutionOperationService(db, events);
            return new(db, service, events, CreateRequest(hostedRunnerId), hostedKey, infraKey, infraId);
        }

        public Task<HostContainerExecutionOperationResponse> QueueAsync()
            => service.QueueAsync(request, request.Scope.RunnerHostId, infrastructureId, CancellationToken.None);

        public Task<HostContainerExecutionOperationResponse> QueueOnDifferentInfrastructureRunnerAsync()
            => service.QueueAsync(request, request.Scope.RunnerHostId, Guid.NewGuid(), CancellationToken.None);

        public async Task<OperationOutcome> ApplyAndReadAsync()
        {
            var operation = await db.HostContainerExecutionOperations.SingleAsync();
            var result = CreateResult(operation);
            var first = await service.ApplyResultAsync(infrastructureKey, operation.Id, result, CancellationToken.None);
            var replay = await service.ApplyResultAsync(infrastructureKey, operation.Id, result, CancellationToken.None);
            var retried = await service.QueueAsync(request, request.Scope.RunnerHostId, operation.InfrastructureRunnerId, CancellationToken.None);
            var visible = await service.GetResultAsync(hostedKey, operation.Id, CancellationToken.None);
            var otherAccount = await service.GetResultAsync(Guid.NewGuid(), operation.Id, CancellationToken.None);
            return new(first, replay, retried, visible, otherAccount, events.Events.Count);
        }

        private static ApplicationDbContext CreateDatabase()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

        private static async Task<(Guid HostedKey, Guid InfrastructureKey, Guid InfrastructureId, Guid HostedRunnerId)> SeedRunnersAsync(ApplicationDbContext db)
        {
            var hostedKey = Guid.NewGuid();
            var infrastructureKey = Guid.NewGuid();
            var infrastructureId = Guid.NewGuid();
            var hostedRunnerId = Guid.NewGuid();
            db.Runners.AddRange(
                new Runner { Id = hostedRunnerId, Name = "hosted", Purpose = RunnerPurposes.Managed, ApiKeyId = hostedKey },
                new Runner { Id = infrastructureId, Name = "infra", Purpose = RunnerPurposes.Infrastructure, ApiKeyId = infrastructureKey },
                new Runner { Id = Guid.NewGuid(), Name = "other-hosted", Purpose = RunnerPurposes.Managed, ApiKeyId = Guid.NewGuid() });
            await db.SaveChangesAsync();
            return (hostedKey, infrastructureKey, infrastructureId, hostedRunnerId);
        }

        private static HostContainerExecutionRequest CreateRequest(Guid hostedRunnerId) => new()
        {
            OperationId = Guid.NewGuid(),
            Scope = new(hostedRunnerId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Operation = HostContainerOperationKinds.ResolveImage,
            RequestedImage = "example/image:1"
        };

        private static HostContainerExecutionResultRequest CreateResult(HostContainerExecutionOperation operation)
            => new(operation.RunnerHostId, operation.InfrastructureRunnerId,
                new HostContainerExecutionResponse { Success = true, ResolvedImage = "example/image@sha256:abc" });

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
        }
    }

    private sealed record OperationOutcome(string FirstApply, string ReplayApply,
        HostContainerExecutionOperationResponse Retried, HostContainerExecutionOperationResponse? Visible,
        HostContainerExecutionOperationResponse? OtherAccount, int PublishedCount);

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<InfrastructureCommandRequestedEvent> Events { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message is InfrastructureCommandRequestedEvent command) Events.Add(command);
            return Task.CompletedTask;
        }
    }
}
