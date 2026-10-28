using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ApiKeyAuditTests
{
    [Test]
    public async Task CreateApiKeyAsync_WritesAuditEventWithoutRawKeyLeakage()
    {
        using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateAuditedService(dbContext, userId);

        var (apiKey, entity) = await service.CreateApiKeyAsync(
            userId,
            "Developer CLI Key",
            ApiKeyScope.Developer);

        Assert.That(entity.Scope, Is.EqualTo(ApiKeyScope.Developer));
        Assert.That(entity.Source, Is.EqualTo(ApiKeySource.Manual));
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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ApiKeyService CreateAuditedService(ApplicationDbContext dbContext, Guid userId)
    {
        var accessor = CreateHttpContextAccessor(userId, "admin@automaton.test");
        var userContext = new UserContextService(accessor);
        var auditEvents = new AuditEventService(dbContext, userContext, accessor);
        return new ApiKeyService(
            dbContext,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditEvents);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId, string userName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName)
        ], "Test"));

        return new HttpContextAccessor { HttpContext = httpContext };
    }
}
