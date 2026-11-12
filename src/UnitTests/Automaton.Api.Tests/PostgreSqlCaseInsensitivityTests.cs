using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

/// <summary>
/// MySQL compared text without case; PostgreSQL does not. Identifier-like columns are citext so
/// every lookup, uniqueness rule and search keeps the behavior it had. Each test stores a value in
/// one case and looks it up in another, using the same query shape as the production code.
/// </summary>
[TestFixture]
public sealed class PostgreSqlCaseInsensitivityTests
{
    private PostgresTestDatabase database = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase() => database = await PostgresTestDatabase.CreateAsync();

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (database is not null) await database.DisposeAsync();
    }

    private static string Unique(string value) => $"{value}-{Guid.NewGuid():N}"[..Math.Min(value.Length + 9, 60)];

    private static ApplicationUser User(string name, string email, string publicUsername) => new()
    {
        Id = Guid.NewGuid(), Name = name, DisplayName = name, Bio = string.Empty, AvatarUrl = string.Empty,
        UserName = name, NormalizedUserName = name.ToUpperInvariant(),
        Email = email, NormalizedEmail = email.ToUpperInvariant(), PublicUsername = publicUsername
    };

    private static WebApp App(string name, string? publicUrl = null) => new()
    {
        Id = Guid.NewGuid(), Name = name, Summary = "Test", Description = "Test", Definition = "{}", PublicUrl = publicUrl
    };

    [Test]
    public async Task Users_AreFoundAndSearchedWithAnyCase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.Open();
        var user = User($"Alice{suffix}", $"Alice.{suffix}@Example.com", $"Alice_{suffix}");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await using var read = database.Open();

        Assert.Multiple(() =>
        {
            // UsersService duplicate checks and UserListingQuery filters.
            Assert.That(read.Users.Any(u => u.UserName == $"ALICE{suffix}".ToLowerInvariant()), Is.True);
            Assert.That(read.Users.Any(u => u.Email == $"ALICE.{suffix}@EXAMPLE.COM"), Is.True);
            Assert.That(read.Users.Any(u => u.PublicUsername != null && u.PublicUsername.ToLower() == $"ALICE_{suffix}".ToLower()), Is.True);
            Assert.That(read.Users.Any(u => u.Name != null && u.Name.StartsWith($"aLiCe{suffix}")), Is.True);
            // AdminAppDirectoryService and UserListingQuery searches.
            Assert.That(read.Users.Any(u => u.Email!.Contains($"EXAMPLE.COM") && u.Id == user.Id), Is.True);
            var term = $"ALICE{suffix}".ToLowerInvariant();
            Assert.That(read.Users.Any(u => u.Name.Contains(term) && u.Id == user.Id), Is.True);
            Assert.That(read.Users.Any(u => u.DisplayName != null && u.DisplayName.Contains(term) && u.Id == user.Id), Is.True);
            Assert.That(read.Users.Any(u => u.PublicUsername != null && u.PublicUsername.Contains($"ALICE_{suffix}".ToUpperInvariant()) && u.Id == user.Id), Is.True);
        });
    }

    [Test]
    public async Task Users_CannotRegisterTheSameNormalizedNameInAnotherCase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.Open();
        db.Users.Add(User($"Bob{suffix}", $"bob{suffix}@example.com", $"bob_{suffix}"));
        await db.SaveChangesAsync();
        await using var second = database.Open();
        second.Users.Add(User($"bob{suffix}", $"BOB{suffix}@example.com", $"other_{suffix}"));
        Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Test]
    public async Task Roles_AreFoundByAnyCase()
    {
        await using var db = database.Open();
        var name = $"Role{Guid.NewGuid():N}"[..20];
        db.Roles.Add(new ApplicationRole { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant() });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        Assert.That(await read.Roles.AnyAsync(role => role.Name == name.ToLowerInvariant()), Is.True);
    }

    [Test]
    public async Task AppNamesAndPublicUrls_AreSearchedAndUniqueWithoutCase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.Open();
        var app = App($"Invoice Tracker {suffix}", $"Invoice-{suffix}");
        db.WebApps.Add(app);
        await db.SaveChangesAsync();
        await using var read = database.Open();

        Assert.Multiple(() =>
        {
            // AppFilesController / AppsService slug lookup, AppPublicationService uniqueness probe.
            Assert.That(read.WebApps.Any(a => a.PublicUrl == $"INVOICE-{suffix}".ToUpperInvariant()), Is.True);
            Assert.That(read.WebApps.Any(a => a.PublicUrl == $"invoice-{suffix}" && a.Id != Guid.NewGuid()), Is.True);
            // AdminAppDirectoryService search.
            var query = $"INVOICE TRACKER {suffix}".ToUpperInvariant();
            Assert.That(read.WebApps.Any(a => a.Name.Contains(query) && a.Id == app.Id), Is.True);
            Assert.That(read.WebApps.Any(a => a.PublicUrl != null && a.PublicUrl.Contains($"INVOICE-{suffix}".ToUpperInvariant()) && a.Id == app.Id), Is.True);
        });

        await using var duplicate = database.Open();
        duplicate.WebApps.Add(App($"Another {suffix}", $"INVOICE-{suffix}"));
        Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Test]
    public async Task DeploymentTargetSlugs_AreUniquePerAppWithoutCase()
    {
        await using var db = database.Open();
        var app = App(Unique("Targets"));
        db.WebApps.Add(app);
        await db.SaveChangesAsync();
        db.AppDeploymentTargets.Add(new AppDeploymentTarget
        {
            Id = Guid.NewGuid(), WebAppId = app.Id, Name = "Staging", Slug = "Staging"
        });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        Assert.That(await read.AppDeploymentTargets.SingleOrDefaultAsync(t => t.WebAppId == app.Id && t.Slug == "STAGING"), Is.Not.Null);

        await using var duplicate = database.Open();
        duplicate.AppDeploymentTargets.Add(new AppDeploymentTarget
        {
            Id = Guid.NewGuid(), WebAppId = app.Id, Name = "Again", Slug = "staging"
        });
        Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Test]
    public async Task SecretNames_AreFoundAndUniquePerOwnerWithoutCase()
    {
        var owner = Guid.NewGuid();
        await using var db = database.Open();
        db.SecretDefinitions.Add(new SecretDefinition { Id = Guid.NewGuid(), OwnerType = "App", OwnerId = owner, Name = "Stripe_Key" });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        // AppSecretService existence check and lookup; Contains over the requested names.
        Assert.That(await read.SecretDefinitions.AnyAsync(s => s.OwnerId == owner && s.Name == "STRIPE_KEY"), Is.True);
        string[] names = ["stripe_key"];
        Assert.That(await read.SecretDefinitions.CountAsync(s => s.OwnerId == owner && names.Contains(s.Name)), Is.EqualTo(1));

        await using var duplicate = database.Open();
        duplicate.SecretDefinitions.Add(new SecretDefinition { Id = Guid.NewGuid(), OwnerType = "App", OwnerId = owner, Name = "STRIPE_KEY" });
        Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Test]
    public async Task SettingKeys_AreFoundAndUniqueWithoutCase()
    {
        var key = Unique("Smtp.Host");
        await using var db = database.Open();
        db.Settings.Add(new Setting { Key = key, Value = "x" });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        Assert.That(await read.Settings.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant()), Is.Not.Null);

        await using var duplicate = database.Open();
        duplicate.Settings.Add(new Setting { Key = key.ToUpperInvariant(), Value = "y" });
        Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Test]
    public async Task OrganizationSlugs_AreFoundAndUniqueWithoutCase()
    {
        var owner = User(Unique("Slug"), $"{Unique("slug")}@example.com", Unique("slug"));
        var slug = Unique("Acme-Corp");
        await using var db = database.Open();
        db.Users.Add(owner);
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), Name = "Acme", OwnerId = owner.Id, Slug = slug, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        Assert.That(await read.Organizations.AnyAsync(o => o.Slug == slug.ToUpperInvariant()), Is.True);

        await using var duplicate = database.Open();
        duplicate.Organizations.Add(new Organization { Id = Guid.NewGuid(), Name = "Acme 2", OwnerId = owner.Id, Slug = slug.ToLowerInvariant(), CreatedAt = DateTime.UtcNow });
        Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Test]
    public async Task ProviderAndRunnerNames_AreFoundWithAnyCase()
    {
        var name = Unique("Nightly-Report");
        await using var db = database.Open();
        db.AiProviders.Add(new AiProvider { Id = Guid.NewGuid(), Name = name, DisplayName = name, CreatedAt = DateTime.UtcNow });
        db.Runners.Add(new Runner { Id = Guid.NewGuid(), Name = name, Status = "online" });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        var other = name.ToUpperInvariant();
        string[] requested = [name.ToLowerInvariant()];
        Assert.Multiple(() =>
        {
            Assert.That(read.AiProviders.Any(p => p.Name == other && p.IsActive), Is.True);
            Assert.That(read.Runners.Any(r => requested.Contains(r.Name) && r.Status == "online"), Is.True);
            Assert.That(read.Runners.Any(r => r.Name == other), Is.True);
        });
    }

    [Test]
    public async Task ChatSessionTitleSearch_IgnoresCase()
    {
        var title = Unique("Quarterly Planning");
        await using var db = database.Open();
        db.AiChatSessions.Add(new AiChatSession
        {
            Id = Guid.NewGuid(), ResourceId = Guid.NewGuid(), Title = title, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        var search = "QUARTERLY PLANNING";
        Assert.That(await read.AiChatSessions.AnyAsync(s => s.Title.Contains(search)), Is.True);
    }

    [Test]
    public async Task CliUserCodes_AreFoundWithAnyCase()
    {
        var code = $"AbC-{Guid.NewGuid():N}"[..12];
        await using var db = database.Open();
        db.CliAuthorizationRequests.Add(new CliAuthorizationRequest
        {
            Id = Guid.NewGuid(), UserCode = code, HashedVerifier = "h", DeviceName = "d",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        Assert.That(await read.CliAuthorizationRequests.AnyAsync(x => x.UserCode == code.ToUpperInvariant()), Is.True);
        Assert.That(await read.CliAuthorizationRequests.AnyAsync(x => x.UserCode == code.ToLowerInvariant()), Is.True);
    }

    [Test]
    public async Task AuditSearch_FindsMetadataWithAnyCase()
    {
        var marker = Guid.NewGuid().ToString("N")[..10];
        await using var db = database.Open();
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), Category = "test", Action = "test", EntityType = "Test", EventType = "test.event",
            Message = "Search", MetadataJson = $"{{\"reason\":\"Failed-{marker}\"}}", CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        var search = $"FAILED-{marker}".ToUpperInvariant();
        // AdminLogsController search over the unbounded metadata column.
        Assert.That(await read.AuditEvents.AnyAsync(x => x.MetadataJson != null
            && x.MetadataJson.ToLower().Contains(search.ToLower())), Is.True);
    }

    [Test]
    public async Task Timestamps_KeepUtcAndMicroseconds()
    {
        var moment = new DateTime(2026, 11, 3, 10, 20, 30, DateTimeKind.Utc).AddTicks(1234560);
        var unspecified = DateTime.SpecifyKind(moment, DateTimeKind.Unspecified);
        var app = App(Unique("Clock"));
        await using var db = database.Open();
        db.WebApps.Add(app);
        await db.SaveChangesAsync();
        var runnerId = Guid.NewGuid();
        db.Runners.Add(new Runner { Id = runnerId, Name = Unique("clock"), Status = "online", LastHeartbeatAtUtc = unspecified });
        db.AppDeploymentTargets.Add(new AppDeploymentTarget
        {
            Id = Guid.NewGuid(), WebAppId = app.Id, Name = "Clock", Slug = "clock",
            LastDeploymentAt = new DateTimeOffset(moment).ToOffset(TimeSpan.FromHours(3))
        });
        await db.SaveChangesAsync();
        await using var read = database.Open();
        var runner = await read.Runners.SingleAsync(r => r.Id == runnerId);
        var target = await read.AppDeploymentTargets.SingleAsync(t => t.WebAppId == app.Id && t.Slug == "clock");
        Assert.Multiple(() =>
        {
            Assert.That(runner.LastHeartbeatAtUtc, Is.EqualTo(moment));
            Assert.That(runner.LastHeartbeatAtUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(target.LastDeploymentAt, Is.EqualTo(new DateTimeOffset(moment)));
            Assert.That(target.LastDeploymentAt!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }
}
