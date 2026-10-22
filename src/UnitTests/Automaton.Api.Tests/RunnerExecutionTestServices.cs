using System.Net;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class RunnerExecutionTestServices
{
    public static InfrastructureActionProvisioningHandler Lifecycle(ApplicationDbContext db,
        IAuditEventService audit, HostedRunnerBrokerProvisioner? broker = null, HostedRunnerOptions? hosted = null)
        => new(db, audit, Options.Create(hosted ?? new HostedRunnerOptions()),
            broker ?? Broker(), Operations(db));
    public static RunnerExecutionRevocationService Revocation(ApplicationDbContext db) => new(Operations(db), Broker());
    public static RunnerExecutionOperationService Operations(ApplicationDbContext db, IEventPublisher? events = null,
        IDataProtectionProvider? protection = null, string? artifactRoot = null)
        => new(db, events ?? new Publisher(), new Transactions(), protection ?? new EphemeralDataProtectionProvider(),
            new RunnerExecutionIdentityService(db, new ResourceAuthorizationService(db, null!)),
            new DeploymentPackageStore(new TestWebHostEnvironment { ContentRootPath = artifactRoot ?? Path.Combine(Path.GetTempPath(), "automaton-runner-execution-tests") },
                Options.Create(new DeploymentPackageStorageOptions())));
    public static HostedRunnerBrokerProvisioner Broker() => new(new Clients(),
        Options.Create(new RabbitMqOptions { ManagementUrl = "http://broker/", Username = "test", Password = "test", VirtualHost = "/" }),
        Options.Create(new HostedRunnerRabbitMqOptions { Host = "broker" }));
    private sealed class Clients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler());
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
    private sealed class Publisher : IEventPublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    }
    private sealed class Transactions : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
