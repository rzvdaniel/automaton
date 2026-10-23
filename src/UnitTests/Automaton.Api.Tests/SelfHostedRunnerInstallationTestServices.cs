using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class SelfHostedRunnerInstallationTestServices
{
    public static SelfHostedRunnerInstallationService CreateService(
        ApplicationDbContext db, Guid userId,
        string serverUrl = "https://api.example.test",
        string dockerImage = "automaton/runner:test")
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test");
        context.User = new ClaimsPrincipal(identity);
        var userContext = new UserContextService(new FixedHttpContextAccessor { HttpContext = context });
        var audit = new RecordingAuditEventService();
        var apiKeys = new ApiKeyService(db, NullLogger<ApiKeyService>.Instance, null!, audit);
        var enrollmentExchange = new SelfHostedRunnerEnrollmentExchangeService(
            db, apiKeys, audit, NullLogger<SelfHostedRunnerEnrollmentExchangeService>.Instance);
        return new SelfHostedRunnerInstallationService(
            db,
            userContext,
            audit,
            enrollmentExchange,
            Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
            Options.Create(new HostedRunnerOptions
            {
                DockerImage = dockerImage,
                ServerUrl = serverUrl
            }));
    }

    public static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"self-hosted-{Guid.NewGuid()}").Options);

    private sealed class RecordingAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
