using Automaton.Api.Constants;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Retention;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public sealed class AdminRetentionControllerTests
{
    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    [Test]
    public void Secret_audit_entity_types_match_the_secret_audit_log()
        => Assert.That(RetentionSettings.SecretAuditEntityTypes, Is.EquivalentTo(
            new[] { AuditEntityTypes.AppSecretVault, AuditEntityTypes.ProjectSecretVault, AuditEntityTypes.UserSecretVault }));

    [Test]
    public async Task Defaults_are_saved_and_long_retention_is_refused_without_approval()
    {
        await using var db = NewDb();
        var controller = new AdminRetentionController(db);
        Assert.That((await controller.GetDefaults(default)).Result, Is.TypeOf<OkObjectResult>());
        Assert.That((await controller.SetDefaults(new RetentionDefaults { RunDays = 91 }, false, default)).Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That((await controller.SetDefaults(new RetentionDefaults { RunDays = 91 }, true, default)).Result, Is.TypeOf<OkObjectResult>());
        Assert.That((await RetentionSettings.GetDefaultsAsync(db, default)).RunDays, Is.EqualTo(91));
    }

    [Test]
    public async Task Unknown_app_override_is_404()
    {
        await using var db = NewDb();
        var controller = new AdminRetentionController(db);
        Assert.That((await controller.GetApp(Guid.NewGuid(), default)).Result, Is.TypeOf<NotFoundResult>());
        Assert.That((await controller.SetApp(Guid.NewGuid(), new RetentionAppOverride { RunDays = 5 }, false, default)).Result, Is.TypeOf<NotFoundResult>());
    }
}
