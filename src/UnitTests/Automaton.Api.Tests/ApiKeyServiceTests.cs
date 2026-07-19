using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

[TestFixture]
public class ApiKeyServiceTests
{
    [Test]
    public void SanitizeMetadataJson_RedactsSensitiveKeys()
    {
        var json = AuditEventService.SanitizeMetadataJson(new
        {
            apiKey = "ak_runner_secret",
            nested = new
            {
                password = "secret",
                safeValue = "ok"
            },
            items = new object[]
            {
                new { authorization = "Bearer abc" },
                new { name = "safe" }
            }
        });

        Assert.That(json, Does.Not.Contain("ak_runner_secret"));
        Assert.That(json, Does.Not.Contain("Bearer abc"));
        Assert.That(json, Does.Not.Contain("\"secret\""));
        Assert.That(json, Does.Contain("[REDACTED]"));
        Assert.That(json, Does.Contain("safeValue"));
        Assert.That(json, Does.Contain("ok"));
    }

    [Test]
    public void SanitizeMetadataJson_RedactsAllProtectedSecretFields()
    {
        var json = AuditEventService.SanitizeMetadataJson(new
        {
            apiKey = "api-key-secret",
            key = "plain-key-secret",
            token = "token-secret",
            secret = "secret-value",
            password = "password-value",
            authorization = "Bearer secret-token",
            connectionString = "Server=db;User Id=app;Password=topsecret;",
            nested = new
            {
                rawKey = "raw-key-secret",
                hash = "hash-secret"
            },
            safe = "visible"
        });

        Assert.That(json, Does.Not.Contain("api-key-secret"));
        Assert.That(json, Does.Not.Contain("plain-key-secret"));
        Assert.That(json, Does.Not.Contain("token-secret"));
        Assert.That(json, Does.Not.Contain("secret-value"));
        Assert.That(json, Does.Not.Contain("password-value"));
        Assert.That(json, Does.Not.Contain("Bearer secret-token"));
        Assert.That(json, Does.Not.Contain("Server=db"));
        Assert.That(json, Does.Not.Contain("raw-key-secret"));
        Assert.That(json, Does.Not.Contain("hash-secret"));
        Assert.That(json, Does.Contain("[REDACTED]"));
        Assert.That(json, Does.Contain("visible"));
    }

    [Test]
    public void GenerateRawKey_ReturnsFixedLengthLowercaseHex()
    {
        var rawKey = ApiKeyService.GenerateRawKey();

        Assert.That(rawKey, Has.Length.EqualTo(40));
        Assert.That(Regex.IsMatch(rawKey, "^[0-9a-f]{40}$"), Is.True);
    }

    [Test]
    public void GenerateRawKey_RemainsFixedLengthAcrossManyIterations()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var rawKey = ApiKeyService.GenerateRawKey();

            Assert.That(rawKey, Has.Length.EqualTo(40), $"Unexpected key length at iteration {i}.");
            Assert.That(Regex.IsMatch(rawKey, "^[0-9a-f]{40}$"), Is.True, $"Unexpected key format at iteration {i}.");
        }
    }

    [Test]
    public async Task CreateApiKeyAsync_WritesAuditEventWithoutRawKeyLeakage()
    {
        using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var httpContextAccessor = CreateHttpContextAccessor(userId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var service = new ApiKeyService(
            dbContext,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditEventService);

        var (apiKey, entity) = await service.CreateApiKeyAsync(userId, "Runner Key");

        Assert.That(entity.Scope, Is.EqualTo(ApiKeyScope.Developer));
        Assert.That(apiKey, Does.StartWith("ak_developer_"));

        var auditEvent = await dbContext.AuditEvents.SingleAsync();

        Assert.That(auditEvent.EventType, Is.EqualTo("ApiKeyCreated"));
        Assert.That(auditEvent.EntityId, Is.EqualTo(entity.Id));
        Assert.That(auditEvent.UserId, Is.EqualTo(userId));
        Assert.That(auditEvent.MetadataJson, Does.Not.Contain(entity.KeyPrefix));
        Assert.That(auditEvent.MetadataJson, Does.Not.Contain(apiKey));
        Assert.That(auditEvent.MetadataJson, Does.Not.Contain(entity.HashedKey));
        Assert.That(auditEvent.MetadataJson, Does.Contain("[REDACTED]"));
    }

    [Test]
    public async Task RevokeInfrastructureRunnerEnrollmentKey_DeactivatesKeyWithoutDeletingHistory()
    {
        using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var httpContextAccessor = CreateHttpContextAccessor(userId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var service = new ApiKeyService(
            dbContext,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditEventService);

        var (_, entity) = await service.CreateApiKeyAsync(
            userId,
            "Infrastructure enrollment",
            scope: ApiKeyScope.InfrastructureRunnerEnroll,
            maximumUses: 1);

        Assert.That(await service.RevokeInfrastructureRunnerEnrollmentKeyAsync(entity.Id, userId), Is.True);

        var stored = await dbContext.ApiKeys.SingleAsync(x => x.Id == entity.Id);
        Assert.That(stored.IsActive, Is.False);
        Assert.That(await dbContext.ApiKeys.CountAsync(), Is.EqualTo(1));
        Assert.That(await dbContext.AuditEvents.AnyAsync(x => x.EntityId == entity.Id && x.Action == "Disabled"), Is.True);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId, string userName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName)
        ], "Test"));

        return new HttpContextAccessor
        {
            HttpContext = httpContext
        };
    }
}
