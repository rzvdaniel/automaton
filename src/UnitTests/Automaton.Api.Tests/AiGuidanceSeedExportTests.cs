using Automaton.AiGuidance;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AiGuidanceSeedExportTests
{
    [Test]
    public void SeedCatalog_ExplicitlyRegistersEveryEmbeddedSeed()
    {
        var provider = new EmbeddedAutomatonGuidanceProvider();
        var embeddedPaths = provider.GetSeedSources().Select(source => source.RelativePath).ToArray();
        var catalogPaths = AutomatonGuidanceSeedCatalog.GetEntries().Select(entry => entry.RelativePath).ToArray();

        Assert.That(embeddedPaths, Is.EqualTo(catalogPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray()));
        Assert.That(catalogPaths, Has.Length.EqualTo(7));
        Assert.That(
            AutomatonGuidanceSeedCatalog.GetEntries()
                .Where(entry => entry.OutputPath is not null)
                .Select(entry => entry.RelativePath)
                .OrderBy(path => path, StringComparer.Ordinal),
            Is.EqualTo(new[]
            {
                "cli/project-files/.automaton/agents/skills/automaton-app/SKILL.md",
                "cli/project-files/AGENTS.md"
            }));
        Assert.That(
            AutomatonGuidanceSeedCatalog.GetEntries()
                .Where(entry => entry.Surface != AutomatonGuidanceSeedCatalog.CliSurface)
                .All(entry => entry.OutputPath is null),
            Is.True);
        Assert.That(() => AutomatonGuidanceSeedCatalog.Resolve("shared/guidance/unknown.md"), Throws.InvalidOperationException);
    }

    [Test]
    public async Task BuildExportPackAsync_ExportsExactlyTheTwoCliProjectFiles()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateRuntimeService(dbContext);
        await service.SeedMissingDefaultsAsync();

        var export = await service.BuildExportPackAsync();
        var files = export.Files.ToDictionary(file => file.TargetPath, file => file.Content, StringComparer.Ordinal);

        Assert.That(files.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray(), Is.EqualTo(new[]
        {
            ".automaton/agents/skills/automaton-app/SKILL.md",
            "AGENTS.md"
        }));

        var exportedText = string.Join("\n", files.Values);
        var normalizedExportedText = exportedText.ReplaceLineEndings(" ");
        Assert.Multiple(() =>
        {
            Assert.That(exportedText, Does.Contain("Dynamic HTTP apps must declare"));
            Assert.That(exportedText, Does.Contain("Static sites use a build command"));
            Assert.That(normalizedExportedText, Does.Contain("shared static server"));
            Assert.That(normalizedExportedText, Does.Contain("Actions-only or other finite-work"));
            Assert.That(exportedText, Does.Contain("no HTTP endpoint"));
            Assert.That(exportedText, Does.Contain("automaton create"));
            Assert.That(exportedText, Does.Contain("not linked in the current environment"));
            Assert.That(exportedText, Does.Not.Contain("When the app has no server-side process"));

            foreach (var staleTerm in new[]
            {
                "\"frontend\":",
                "\"backend\":",
                "main:app",
                "FastAPI",
                "Default to frontend-only",
                "frontend/",
                "backend/",
                "Vite",
                "React",
                "Vue"
            })
            {
                Assert.That(exportedText, Does.Not.Contain(staleTerm), staleTerm);
            }

            foreach (var requiredTerm in new[]
            {
                "application",
                "build",
                "runtime",
                "health",
                "storage",
                "automaton validate",
                "automaton deploy",
                "Succeeded",
                "publish"
            })
            {
                Assert.That(exportedText, Does.Contain(requiredTerm), requiredTerm);
            }
        });
    }

    [Test]
    public async Task BuildExportPackAsync_ExcludesNonCliDocumentsWithOutputPaths()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AiGuidanceDocuments.AddRange(
            new AiGuidanceDocument
            {
                Id = Guid.NewGuid(), Key = "admin-studio", Title = "Admin Studio", Surface = "studio",
                DocumentKind = AiGuidanceDocumentKinds.Guidance, OutputPath = "studio.md", ContentMarkdown = "studio",
                IsEnabled = true, IsModelLoadable = true, Version = 1, CreatedAt = DateTime.UtcNow
            },
            new AiGuidanceDocument
            {
                Id = Guid.NewGuid(), Key = "admin-cli", Title = "Admin CLI", Surface = "cli",
                DocumentKind = AiGuidanceDocumentKinds.Guidance, OutputPath = "custom.md", ContentMarkdown = "custom",
                IsEnabled = true, IsModelLoadable = true, Version = 1, CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var export = await CreateRuntimeService(dbContext).BuildExportPackAsync();

        Assert.That(export.Files.Select(file => file.TargetPath), Is.EqualTo(new[] { "custom.md" }));
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
