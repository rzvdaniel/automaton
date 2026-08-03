using Automaton.AiGuidance;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AiGuidanceSeedExportTests
{
    [Test]
    public async Task SeedMissingDefaultsAsync_SeedsPrivilegedHostGuidance_WithExpectedMetadataAndContent()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateRuntimeService(dbContext);

        await service.SeedMissingDefaultsAsync();

        var document = await dbContext.AiGuidanceDocuments
            .SingleAsync(item => item.Key == "automaton-privileged-host-apps");

        Assert.Multiple(() =>
        {
            Assert.That(document.Title, Is.EqualTo("Automaton Privileged Host Apps"));
            Assert.That(document.Surface, Is.EqualTo("shared"));
            Assert.That(document.DocumentKind, Is.EqualTo(AiGuidanceDocumentKinds.Guidance));
            Assert.That(document.RequiredByDefault, Is.False);
            Assert.That(document.IsModelLoadable, Is.True);
            Assert.That(document.OutputPath, Is.EqualTo(".automaton/agents/references/llms/automaton-privileged-host-apps.md"));
            Assert.That(document.ContentMarkdown, Does.Contain("/host"));
            Assert.That(document.ContentMarkdown, Does.Contain("/var/run/docker.sock"));
            Assert.That(document.ContentMarkdown, Does.Contain("shell=False"));
            Assert.That(document.ContentMarkdown, Does.Contain("Infrastructure Runners are different roles"));
        });
    }

    [Test]
    public async Task BuildExportPackAsync_ExportsConsistentBackendContractAndPrivilegedHostTrigger()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateRuntimeService(dbContext);
        await service.SeedMissingDefaultsAsync();

        var export = await service.BuildExportPackAsync();
        var files = export.Files.ToDictionary(file => file.TargetPath, file => file.Content, StringComparer.Ordinal);

        var skill = files[".automaton/agents/skills/automaton-app/SKILL.md"];
        var agents = files["AGENTS.md"];
        var privilegedHostGuide = files[".automaton/agents/references/llms/automaton-privileged-host-apps.md"];

        Assert.Multiple(() =>
        {
            Assert.That(skill, Does.Contain("Automaton backends are ordinary HTTP applications"));
            Assert.That(skill, Does.Contain("FastAPI/ASGI with an entrypoint of `main:app`"));
            Assert.That(skill, Does.Not.Contain("Never create FastAPI"));
            Assert.That(skill, Does.Contain("automaton-privileged-host-apps.md"));
            Assert.That(agents, Does.Contain("automaton-privileged-host-apps.md"));
            Assert.That(privilegedHostGuide, Does.Contain("fixed subprocess only when no stable file or API is suitable"));
            Assert.That(privilegedHostGuide, Does.Contain("- expose an endpoint that accepts commands;"));
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AiGuidanceRuntimeService CreateRuntimeService(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new EmbeddedAutomatonGuidanceProvider(),
            NullLogger<AiGuidanceRuntimeService>.Instance);
}
