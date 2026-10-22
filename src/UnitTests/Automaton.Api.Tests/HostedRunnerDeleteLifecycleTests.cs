using System.Security.Claims;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;



namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerDeleteLifecycleTests
{


    [Test]
    public async Task SuccessfulHostedRunnerDeleteReleasesRuntimePort()
    {
        var result = await HostedRunnerDeleteLifecycleTestSupport.RunSuccessfulDeleteAsync();

        Assert.That(result.RunnerDeleted, Is.True);
        Assert.That(result.RuntimePortCleared, Is.True);
        Assert.That(result.BrokerUserDeleted, Is.True);
        Assert.That(result.BrokerQueueDeleted, Is.True);
        Assert.That(result.ExecutionBrokerRevoked, Is.True);
    }
}

internal static class HostedRunnerDeleteLifecycleTestSupport
{
    public static async Task<HostedRunnerDeleteLifecycleTestResult> RunSuccessfulDeleteAsync()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var runnerId = Guid.NewGuid();
        var brokerHandler = new RecordingBrokerHandler();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "delete-port",
            RunnerId = runnerId, ContainerName = "delete-port", RuntimePort = 20000,
            Status = HostedRunnerStatuses.Deleting
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Deprovision,
            Status = HostedRunnerDeploymentStatuses.Executing
        };
        var handler = RunnerExecutionTestServices.Lifecycle(
            db, new HostedRunnerAuditNoOpAuditEventService(), CreateBrokerProvisioner(brokerHandler));
        using var document = JsonDocument.Parse("{\"success\":true}");

        await handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "delete-hosted-runner", document.RootElement, CancellationToken.None);

        return new HostedRunnerDeleteLifecycleTestResult(
            hostedRunner.Status == HostedRunnerStatuses.Deleted,
            hostedRunner.RuntimePort is null,
            brokerHandler.Requests.Count == 5
                && brokerHandler.Requests[3].Method == HttpMethod.Delete
                && brokerHandler.Requests[3].Path.Contains($"hosted-runner-{runnerId:N}", StringComparison.Ordinal),
            brokerHandler.Requests.Count == 5
                && brokerHandler.Requests[4].Method == HttpMethod.Delete
                && brokerHandler.Requests[4].Path.Contains(
                    HostedRunnerBrokerProvisioner.LegacyDeploymentQueueName(runnerId), StringComparison.Ordinal),
            ExecutionBrokerRevoked(brokerHandler.Requests, runnerId));
    }

    private static bool ExecutionBrokerRevoked(List<(HttpMethod Method, string Path)> requests, Guid runnerId)
        => requests.Count == 5
            && requests.Take(3).All(r => r.Method == HttpMethod.Delete)
            && requests[0].Path.Contains($"execution-runner-{runnerId:N}", StringComparison.Ordinal)
            && requests[1].Path.Contains(RunnerExecutionCommandPublisher.QueueName(runnerId), StringComparison.Ordinal)
            && requests[2].Path.Contains(RunnerExecutionCommandPublisher.ManagementQueueName(runnerId), StringComparison.Ordinal);

    private static HostedRunnerBrokerProvisioner CreateBrokerProvisioner(RecordingBrokerHandler handler)
        => new(
            new RecordingClientFactory(new HttpClient(handler)),
            Options.Create(new RabbitMqOptions
            {
                Username = "operator",
                Password = "operator-password",
                VirtualHost = "/",
                ManagementUrl = "http://rabbitmq:15672/"
            }),
            Options.Create(new HostedRunnerRabbitMqOptions { Host = "broker.example", Port = 5672 }));

    private sealed class RecordingClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingBrokerHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}

internal sealed record HostedRunnerDeleteLifecycleTestResult(
    bool RunnerDeleted,
    bool RuntimePortCleared,
    bool BrokerUserDeleted,
    bool BrokerQueueDeleted,
    bool ExecutionBrokerRevoked);
