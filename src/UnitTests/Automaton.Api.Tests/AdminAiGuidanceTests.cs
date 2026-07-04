using Automaton.AiGuidance;
using Automaton.Api.Constants;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public class AdminAiGuidanceTests
{
    [Test]
    public async Task CreateAsync_CreatesDocument_WithVersionOne()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateAdminService(dbContext);

        var document = await service.CreateAsync(
            "studio-editor-extra",
            "llms/studio-editor-extra.md",
            "Studio Editor Extra",
            "studio",
            AiGuidanceDocumentKinds.Guidance,
            "Extra editor guidance",
            null,
            "# Guidance",
            true,
            false,
            true,
            100);

        Assert.Multiple(() =>
        {
            Assert.That(document.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(document.Version, Is.EqualTo(1));
            Assert.That(document.IsEnabled, Is.True);
            Assert.That(document.ContentMarkdown, Does.StartWith("---"));
            Assert.That(document.ContentMarkdown, Does.Contain("title: \"Studio Editor Extra\""));
            Assert.That(dbContext.AiGuidanceDocuments.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UpdateAsync_BumpsVersion()
    {
        await using var dbContext = CreateDbContext();
        var entity = await SeedGuidanceDocumentAsync(dbContext);
        var service = CreateAdminService(dbContext);

        var updated = await service.UpdateAsync(
            entity.Id,
            entity.Key,
            entity.RelativePath,
            "Updated Title",
            entity.Surface,
            entity.DocumentKind,
            entity.Description,
            entity.TagsJson,
            "# Updated",
            entity.IsEnabled,
            entity.RequiredByDefault,
            entity.IsModelLoadable,
            entity.SortOrder);

        Assert.That(updated, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(updated!.Version, Is.EqualTo(2));
            Assert.That(updated.Title, Is.EqualTo("Updated Title"));
            Assert.That(updated.ContentMarkdown, Does.Contain("title: \"Updated Title\""));
            Assert.That(updated.ContentMarkdown, Does.Contain("# Updated"));
        });
    }

    [Test]
    public async Task SetEnabledAsync_EnableDisable_BumpsVersion()
    {
        await using var dbContext = CreateDbContext();
        var entity = await SeedGuidanceDocumentAsync(dbContext, isEnabled: true);
        var service = CreateAdminService(dbContext);

        var disabled = await service.SetEnabledAsync(entity.Id, false);
        var enabled = await service.SetEnabledAsync(entity.Id, true);

        Assert.Multiple(() =>
        {
            Assert.That(disabled, Is.Not.Null);
            Assert.That(disabled!.IsEnabled, Is.False);
            Assert.That(disabled.Version, Is.EqualTo(2));
            Assert.That(enabled, Is.Not.Null);
            Assert.That(enabled!.IsEnabled, Is.True);
            Assert.That(enabled.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ResetToEmbeddedDefaultAsync_RestoresEmbeddedContent_AndBumpsVersion()
    {
        await using var dbContext = CreateDbContext();
        var entity = await SeedGuidanceDocumentAsync(
            dbContext,
            key: "automaton-studio-editor",
            relativePath: "llms/automaton-studio-editor.md",
            title: "Automaton Studio Editor",
            contentMarkdown: "# Overridden");
        var service = CreateAdminService(dbContext);

        var reset = await service.ResetToEmbeddedDefaultAsync(entity.Id);

        Assert.That(reset, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reset!.Version, Is.EqualTo(2));
            Assert.That(reset.ContentMarkdown, Does.Contain("# Automaton Studio App Editor"));
            Assert.That(reset.ContentMarkdown, Does.Not.EqualTo("# Overridden"));
        });
    }

    [Test]
    public async Task Create_DuplicateKey_ReturnsCleanValidationError()
    {
        await using var dbContext = CreateDbContext();
        await SeedGuidanceDocumentAsync(dbContext, key: "duplicate-key", relativePath: "llms/duplicate-a.md");
        var controller = CreateController(dbContext);

        var result = await controller.Create(new UpsertAiGuidanceRequest
        {
            Key = "duplicate-key",
            RelativePath = "llms/duplicate-b.md",
            Title = "Duplicate",
            Surface = "studio",
            DocumentKind = AiGuidanceDocumentKinds.Guidance,
            ContentMarkdown = "# Duplicate",
            IsEnabled = true,
            IsModelLoadable = true,
            SortOrder = 100
        }, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("duplicate-key"));
    }

    [Test]
    public async Task ListLoadableGuidanceAsync_ReturnsOnlyEnabledModelLoadableGuidance_ForRequestedSurface()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AiGuidanceDocuments.AddRange(
            new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = "studio-a",
                RelativePath = "llms/studio-a.md",
                Title = "Studio A",
                Surface = "studio",
                DocumentKind = AiGuidanceDocumentKinds.Guidance,
                TagsJson = """["editor"]""",
                ContentMarkdown = "# A",
                IsEnabled = true,
                IsModelLoadable = true,
                SortOrder = 100,
                Version = 1,
                CreatedAt = DateTime.UtcNow
            },
            new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = "studio-b-disabled",
                RelativePath = "llms/studio-b.md",
                Title = "Studio B",
                Surface = "studio",
                DocumentKind = AiGuidanceDocumentKinds.Guidance,
                TagsJson = """["editor"]""",
                ContentMarkdown = "# B",
                IsEnabled = false,
                IsModelLoadable = true,
                SortOrder = 100,
                Version = 1,
                CreatedAt = DateTime.UtcNow
            },
            new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = "cli-a",
                RelativePath = "llms/cli-a.md",
                Title = "CLI A",
                Surface = "cli",
                DocumentKind = AiGuidanceDocumentKinds.Guidance,
                TagsJson = """["deploy"]""",
                ContentMarkdown = "# CLI",
                IsEnabled = true,
                IsModelLoadable = true,
                SortOrder = 100,
                Version = 1,
                CreatedAt = DateTime.UtcNow
            },
            new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = "studio-base",
                RelativePath = "llms/studio-base.md",
                Title = "Studio Base",
                Surface = "studio",
                DocumentKind = AiGuidanceDocumentKinds.BasePrompt,
                ContentMarkdown = "# Base",
                IsEnabled = true,
                RequiredByDefault = true,
                IsModelLoadable = false,
                SortOrder = 0,
                Version = 1,
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var runtimeService = CreateRuntimeService(dbContext);
        var documents = await runtimeService.ListLoadableGuidanceAsync("studio");

        Assert.That(documents.Select(x => x.Key).ToArray(), Is.EqualTo(new[] { "studio-a" }));
    }

    [Test]
    public async Task GetGuidanceByRelativePathAsync_ReturnsDatabaseDocument_WhenEnabledAndModelLoadable()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AiGuidanceDocuments.Add(new AiGuidanceDocument
        {
            Id = Guid.NewGuid(),
            Key = "automaton-studio-editor",
            RelativePath = "llms/automaton-studio-editor.md",
            Title = "Automaton Studio Editor",
            Surface = "studio",
            DocumentKind = AiGuidanceDocumentKinds.Guidance,
            TagsJson = """["editor"]""",
            ContentMarkdown = "# Database Override",
            IsEnabled = true,
            IsModelLoadable = true,
            SortOrder = 100,
            Version = 7,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var runtimeService = CreateRuntimeService(dbContext);
        var document = await runtimeService.GetGuidanceByRelativePathAsync("llms/automaton-studio-editor.md");

        Assert.That(document, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(document!.IsDatabaseDocument, Is.True);
            Assert.That(document.Version, Is.EqualTo(7));
            Assert.That(document.ContentMarkdown, Is.EqualTo("# Database Override"));
        });
    }

    [Test]
    public async Task GetGuidanceByRelativePathAsync_ReturnsNull_WhenDatabaseDocMissing()
    {
        await using var dbContext = CreateDbContext();
        var runtimeService = CreateRuntimeService(dbContext);

        var document = await runtimeService.GetGuidanceByRelativePathAsync("llms/automaton-studio-editor.md");

        Assert.That(document, Is.Null);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AdminAiGuidanceService CreateAdminService(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new EmbeddedAutomatonGuidanceProvider(),
            new FakeAuditEventService());

    private static AiGuidanceRuntimeService CreateRuntimeService(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new EmbeddedAutomatonGuidanceProvider(),
            NullLogger<AiGuidanceRuntimeService>.Instance);

    private static AdminAiGuidanceController CreateController(ApplicationDbContext dbContext) =>
        new(CreateAdminService(dbContext));

    private static async Task<AiGuidanceDocument> SeedGuidanceDocumentAsync(
        ApplicationDbContext dbContext,
        string key = "seed-key",
        string relativePath = "llms/seed.md",
        string title = "Seed Title",
        string contentMarkdown = "# Seed",
        bool isEnabled = true)
    {
        var entity = new AiGuidanceDocument
        {
            Id = Guid.NewGuid(),
            Key = key,
            RelativePath = relativePath,
            Title = title,
            Surface = "studio",
            DocumentKind = AiGuidanceDocumentKinds.Guidance,
            TagsJson = """["editor"]""",
            ContentMarkdown = contentMarkdown,
            IsEnabled = isEnabled,
            IsModelLoadable = true,
            SortOrder = 100,
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiGuidanceDocuments.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    private sealed class FakeAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
