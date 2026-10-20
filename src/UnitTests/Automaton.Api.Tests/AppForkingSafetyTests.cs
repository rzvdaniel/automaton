using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppForkingSafetyTests
{
    [Test]
    public async Task ForkPreviewAndForkAsync_DoNotExposeOrCopyContractEnvironmentValues()
    {
        await using var fixture = await CopyFixture.CreateAsync(compose: true);

        var preview = await fixture.Apps.GetForkSourceAsync(fixture.Source.Id);
        var fork = await fixture.Apps.ForkAsync(fixture.Source.Id);
        var copiedContract = ApplicationContractSerialization.Deserialize(
            (await fixture.Db.WebApps.SingleAsync(app => app.Id == fork.Id)).ApplicationContractJson);

        AssertEnvironmentCleared(preview.Application!);
        AssertEnvironmentCleared(copiedContract);
    }

    private static void AssertEnvironmentCleared(ApplicationContract contract)
    {
        if (contract.Build.Command is not null)
            Assert.That(contract.Build.Command?.Environment, Is.Empty);
        if (contract.Runtime.Command is not null)
            Assert.That(contract.Runtime.Command?.Environment, Is.Empty);
        if (contract.Compose is not null)
            Assert.That(contract.Compose.Services.Single().Environment, Is.Empty);
    }

    private sealed class CopyFixture : IAsyncDisposable
    {
        private CopyFixture(
            Automaton.Api.Data.ApplicationDbContext db,
            AppsService apps,
            WebApp source,
            string root)
        {
            Db = db;
            Apps = apps;
            Source = source;
            Root = root;
        }

        public Automaton.Api.Data.ApplicationDbContext Db { get; }
        public AppsService Apps { get; }
        public WebApp Source { get; }
        private string Root { get; }

        public static async Task<CopyFixture> CreateAsync(bool compose)
        {
            var root = Path.Combine(Path.GetTempPath(), "automaton-app-copy-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var db = AppAuthorizationTestData.CreateDbContext();
            var ownerId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var source = new WebApp
            {
                Id = sourceId,
                Name = "Source",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                ApplicationContractJson = ApplicationContractSerialization.Serialize(CreateContract(compose)),
                WebAppUsers = [],
                IsPublished = true,
                IsForkable = true,
                PublicUrl = "copy-source",
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };
            db.WebApps.Add(source);
            db.WebAppUsers.Add(new WebAppUser { WebAppId = sourceId, UserId = ownerId });
            await db.SaveChangesAsync();
            if (!await db.WebApps.AnyAsync(app =>
                    app.Id == sourceId && app.WebAppUsers.Any(user => user.UserId == ownerId)))
                throw new InvalidOperationException("The copy test fixture did not expose the app-owner association.");
            var apps = AppAuthorizationTestServiceFactory.CreateAppsService(db, ownerId, root, out var storage);
            await storage.WriteSourceFileAsync(ownerId, sourceId, "index.html", "safe source");
            return new CopyFixture(db, apps, source, root);
        }

        private static ApplicationContract CreateContract(bool compose) => new()
        {
            Build = new ApplicationBuildSpec
            {
                Command = compose ? null : new ApplicationCommandSpec
                {
                    Executable = "npm",
                    Environment = new Dictionary<string, string> { ["BUILD_TOKEN"] = "build-secret" }
                }
            },
            Runtime = new ApplicationRuntimeSpec
            {
                Command = compose ? null : new ApplicationCommandSpec
                {
                    Executable = "node",
                    Environment = new Dictionary<string, string> { ["RUNTIME_TOKEN"] = "runtime-secret" }
                }
            },
            Compose = compose ? new ApplicationComposeSpec
            {
                PublicService = "web",
                Services =
                [
                    new ApplicationComposeService
                    {
                        Name = "web",
                        Image = "nginx:alpine",
                        Port = 80,
                        Environment = new Dictionary<string, string> { ["COMPOSE_TOKEN"] = "compose-secret" }
                    }
                ]
            } : null
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
