using System.Security.Claims;
using Automaton.Api.Constants;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Retention;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public sealed class AdminRetentionControllerTests
{
    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static AdminRetentionController Controller(ApplicationDbContext db)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Name, "admin@automaton.test")], "Test"))
        } };
        var userContext = new UserContextService(accessor);
        return new AdminRetentionController(db, new AuditEventService(db, userContext, accessor), userContext);
    }

    [Test]
    public async Task Changes_are_audited_and_audit_retention_has_a_minimum()
    {
        await using var db = NewDb();
        var controller = Controller(db);
        Assert.That((await controller.SetDefaults(new RetentionDefaults { AuditDays = 1 }, true, default)).Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(await db.AuditEvents.CountAsync(), Is.EqualTo(0));
        Assert.That((await controller.SetDefaults(new RetentionDefaults { AuditDays = 30 }, false, default)).Result, Is.TypeOf<OkObjectResult>());
        var app = Guid.NewGuid();
        db.WebApps.Add(new Automaton.Api.Entities.WebApp { Id = app });
        await db.SaveChangesAsync();
        Assert.That((await controller.SetApp(app, new RetentionAppOverride { RunDays = 5 }, false, default)).Result, Is.TypeOf<OkObjectResult>());
        await controller.ClearApp(app, default);
        var events = await db.AuditEvents.Where(x => x.EventType == AuditEventTypes.RetentionChanged).ToListAsync();
        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events.All(x => x.UserId != null), Is.True);
    }

    [Test]
    public void Secret_audit_entity_types_match_the_secret_audit_log()
        => Assert.That(RetentionSettings.SecretAuditEntityTypes, Is.EquivalentTo(
            new[] { AuditEntityTypes.AppSecretVault, AuditEntityTypes.ProjectSecretVault, AuditEntityTypes.UserSecretVault }));

    [Test]
    public async Task Defaults_are_saved_and_long_retention_is_refused_without_approval()
    {
        await using var db = NewDb();
        var controller = Controller(db);
        Assert.That((await controller.GetDefaults(default)).Result, Is.TypeOf<OkObjectResult>());
        Assert.That((await controller.SetDefaults(new RetentionDefaults { RunDays = 91 }, false, default)).Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That((await controller.SetDefaults(new RetentionDefaults { RunDays = 91 }, true, default)).Result, Is.TypeOf<OkObjectResult>());
        Assert.That((await RetentionSettings.GetDefaultsAsync(db, default)).RunDays, Is.EqualTo(91));
    }

    [Test]
    public async Task Unknown_app_override_is_404()
    {
        await using var db = NewDb();
        var controller = Controller(db);
        Assert.That((await controller.GetApp(Guid.NewGuid(), default)).Result, Is.TypeOf<NotFoundResult>());
        Assert.That((await controller.SetApp(Guid.NewGuid(), new RetentionAppOverride { RunDays = 5 }, false, default)).Result, Is.TypeOf<NotFoundResult>());
    }
}
