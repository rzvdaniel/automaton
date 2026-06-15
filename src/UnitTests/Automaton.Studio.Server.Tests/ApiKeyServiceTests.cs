using System.Security.Claims;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Tests;

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

        var auditEvent = await dbContext.AuditEvents.SingleAsync();

        Assert.That(auditEvent.EventType, Is.EqualTo("ApiKeyCreated"));
        Assert.That(auditEvent.EntityId, Is.EqualTo(entity.Id));
        Assert.That(auditEvent.UserId, Is.EqualTo(userId));
        Assert.That(auditEvent.MetadataJson, Does.Contain(entity.KeyPrefix));
        Assert.That(auditEvent.MetadataJson, Does.Not.Contain(apiKey));
        Assert.That(auditEvent.MetadataJson, Does.Not.Contain(entity.HashedKey));
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
