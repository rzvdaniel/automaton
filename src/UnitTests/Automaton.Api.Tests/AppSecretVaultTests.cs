using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

/// <summary>Shared project and user vaults: declared names only, app secrets first, no cross-person leaks.</summary>
public sealed class AppSecretVaultTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task OneProjectSecretServesTwoAppsAndRotatingItUpdatesBoth()
    {
        var fixture = Fixture.Create();
        var first = fixture.AddProjectApp(Declare("STRIPE_KEY"));
        var second = fixture.AddProjectApp(Declare("STRIPE_KEY"));

        await fixture.Service.SetAsync(first, fixture.Developer, AppSecretScopes.Project, "STRIPE_KEY", "v1", "Stripe live key", None);
        Assert.That((await fixture.Service.ResolveForExecutionAsync(first, None))["STRIPE_KEY"], Is.EqualTo("v1"));
        Assert.That((await fixture.Service.ResolveForExecutionAsync(second, None))["STRIPE_KEY"], Is.EqualTo("v1"));

        await fixture.Service.SetAsync(second, fixture.Developer, AppSecretScopes.Project, "STRIPE_KEY", "v2", null, None);
        Assert.That((await fixture.Service.ResolveForExecutionAsync(first, None))["STRIPE_KEY"], Is.EqualTo("v2"));
        Assert.That((await fixture.Service.ResolveForExecutionAsync(second, None))["STRIPE_KEY"], Is.EqualTo("v2"));
        var listed = await fixture.Service.ListAsync(first, fixture.Developer, AppSecretScopes.Project, None);
        Assert.That(listed.Single().Description, Is.EqualTo("Stripe live key"), "a later set without a description keeps it");
    }

    [Test]
    public async Task UndeclaredVaultSecretsNeverReachAnApp()
    {
        var fixture = Fixture.Create();
        var declares = fixture.AddProjectApp(Declare("STRIPE_KEY"));
        var silent = fixture.AddProjectApp();
        await fixture.Service.SetAsync(declares, fixture.Developer, AppSecretScopes.Project, "STRIPE_KEY", "value", null, None);
        await fixture.Service.SetAsync(declares, fixture.Developer, AppSecretScopes.Project, "OTHER", "value", null, None);

        Assert.That((await fixture.Service.ResolveForExecutionAsync(declares, None)).Keys, Is.EquivalentTo(new[] { "STRIPE_KEY" }));
        Assert.That(await fixture.Service.ResolveForExecutionAsync(silent, None), Is.Empty);
    }

    [Test]
    public async Task AppSecretsWinAndFromMapsAnotherVaultName()
    {
        var fixture = Fixture.Create();
        var app = fixture.AddProjectApp(Declare("STRIPE_KEY"), Declare("PAYMENTS_KEY", "project:STRIPE_LIVE_KEY"));
        await fixture.Service.SetAsync(app, fixture.Developer, AppSecretScopes.Project, "STRIPE_KEY", "shared", null, None);
        await fixture.Service.SetAsync(app, fixture.Developer, AppSecretScopes.Project, "STRIPE_LIVE_KEY", "live", null, None);
        await fixture.Service.SetAsync(app, fixture.Developer, "STRIPE_KEY", "own", None);

        var resolved = await fixture.Service.ResolveForExecutionAsync(app, None);
        Assert.That(resolved["STRIPE_KEY"], Is.EqualTo("own"));
        Assert.That(resolved["PAYMENTS_KEY"], Is.EqualTo("live"));
        Assert.That(resolved.ContainsKey("STRIPE_LIVE_KEY"), Is.False);
    }

    [Test]
    public async Task UserVaultFillsOnlyPersonalAppsOfThatOwner()
    {
        var fixture = Fixture.Create();
        var personal = fixture.AddPersonalApp(fixture.Developer, Declare("OPENAI_KEY"));
        // The Developer's own vault must not reach a project app: other project members can change its code.
        var project = fixture.AddProjectApp(Declare("OPENAI_KEY"));
        await fixture.Service.SetAsync(personal, fixture.Developer, AppSecretScopes.User, "OPENAI_KEY", "mine", null, None);

        Assert.That((await fixture.Service.ResolveForExecutionAsync(personal, None))["OPENAI_KEY"], Is.EqualTo("mine"));
        Assert.That(await fixture.Service.ResolveForExecutionAsync(project, None), Is.Empty);
        var status = await fixture.Service.StatusAsync(project, fixture.Developer, [Declare("OPENAI_KEY", "user:OPENAI_KEY")], None);
        Assert.That(status.Single().Problem, Does.Contain("personal apps"));
    }

    [Test]
    public async Task StatusReportsPresenceAndProviderWithoutValues()
    {
        var fixture = Fixture.Create();
        var app = fixture.AddProjectApp();
        await fixture.Service.SetAsync(app, fixture.Developer, "OWN", "value", None);
        await fixture.Service.SetAsync(app, fixture.Developer, AppSecretScopes.Project, "SHARED", "value", null, None);

        var status = await fixture.Service.StatusAsync(app, fixture.Developer,
            [Declare("OWN"), Declare("SHARED"), Declare("MISSING")], None);

        Assert.That(status.Select(item => (item.Name, item.Present, item.ProvidedBy)), Is.EqualTo(new[]
        {
            ("OWN", true, (string?)AppSecretScopes.App),
            ("SHARED", true, (string?)AppSecretScopes.Project),
            ("MISSING", false, (string?)null)
        }));
    }

    [Test]
    public async Task OnlyProjectDevelopersChangeTheProjectVault()
    {
        var fixture = Fixture.Create();
        var app = fixture.AddProjectApp();
        var viewer = fixture.AddMember(ProjectRole.Viewer);
        // A viewer cannot update the app at all, so cannot reach its project vault through it.
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.SetAsync(app, viewer, AppSecretScopes.Project, "KEY", "value", null, None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.ListAsync(app, Guid.NewGuid(), AppSecretScopes.Project, None));
        Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.SetAsync(fixture.AddPersonalApp(fixture.Developer), fixture.Developer, AppSecretScopes.Project, "KEY", "value", null, None));
        Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.SetAsync(app, fixture.Developer, "nope", "KEY", "value", null, None));
        Assert.That(await fixture.Db.SecretDefinitions.CountAsync(), Is.Zero);
    }

    [TestCase("lowercase", null)]
    [TestCase("AUTOMATON_PORT", null)]
    [TestCase("KEY", "vault:KEY")]
    [TestCase("KEY", "project:lower")]
    public void ContractRejectsUnusableDeclarations(string name, string? from)
    {
        var errors = ApplicationContractValidator.Validate(new ApplicationContract { Secrets = [Declare(name, from)] });
        Assert.That(errors, Has.Some.StartsWith("secrets"));
    }

    [Test]
    public void ContractRejectsDuplicateDeclarations()
        => Assert.That(ApplicationContractValidator.Validate(new ApplicationContract { Secrets = [Declare("KEY"), Declare("KEY")] }),
            Has.Some.Contains("more than once"));

    private static ApplicationSecretDeclaration Declare(string name, string? from = null) => new() { Name = name, From = from };

    private sealed class Fixture
    {
        public required ApplicationDbContext Db { get; init; }
        public required AppSecretService Service { get; init; }
        public required Guid ProjectId { get; init; }
        public required Guid Developer { get; init; }

        public static Fixture Create()
        {
            var db = AppAuthorizationTestData.CreateDbContext();
            var projectId = Guid.NewGuid();
            var developer = Guid.NewGuid();
            db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, Guid.NewGuid(), "project"));
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId, UserId = developer, Role = ProjectRole.Developer, JoinedAt = DateTime.UtcNow, IsActive = true
            });
            db.SaveChanges();
            var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
            var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(developer));
            return new Fixture
            {
                Db = db,
                Service = new AppSecretService(db, new SecretStore(db, protector), protector, authorization,
                    new SecretAuditLog(new AuditEventService(db, AppAuthorizationTestData.CreateUserContextService(developer), new HttpContextAccessor()), db)),
                ProjectId = projectId, Developer = developer
            };
        }

        public Guid AddMember(ProjectRole role)
        {
            var user = Guid.NewGuid();
            Db.ProjectMembers.Add(new ProjectMember { ProjectId = ProjectId, UserId = user, Role = role, JoinedAt = DateTime.UtcNow, IsActive = true });
            Db.SaveChanges();
            return user;
        }

        public Guid AddProjectApp(params ApplicationSecretDeclaration[] secrets)
        {
            var appId = AddPersonalApp(Developer, secrets);
            Db.ProjectApps.Add(new ProjectApp { ProjectId = ProjectId, WebAppId = appId, AddedAt = DateTime.UtcNow });
            Db.SaveChanges();
            return appId;
        }

        public Guid AddPersonalApp(Guid owner, params ApplicationSecretDeclaration[] secrets)
        {
            var appId = Guid.NewGuid();
            AppAuthorizationTestData.SeedApp(Db, appId, owner, $"app-{appId:N}");
            var app = Db.WebApps.Single(item => item.Id == appId);
            app.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract { Secrets = [.. secrets] });
            Db.SaveChanges();
            return appId;
        }
    }
}
