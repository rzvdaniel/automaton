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
            "Studio Editor Extra",
            "studio",
            AiGuidanceDocumentKinds.Guidance,
            "Extra editor guidance",
            null,
            ".automaton/agents/references/llms/studio-editor-extra.md",
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
            Assert.That(document.ContentMarkdown, Is.EqualTo("# Guidance"));
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
            "Updated Title",
            entity.Surface,
            entity.DocumentKind,
            entity.Description,
            entity.TagsJson,
            entity.OutputPath,
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
            Assert.That(updated.ContentMarkdown, Is.EqualTo("# Updated"));
        });
    }

    [Test]
    public async Task SetEnabledAsync_EnableDisable_BumpsVersion()
    {
        await using var dbContext = CreateDbContext();
        var entity = await SeedGuidanceDocumentAsync(dbContext, isEnabled: true);
        var service = CreateAdminService(dbContext);

        var disabled = await service.SetEnabledAsync(entity.Id, false);
        Assert.That(disabled, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(disabled!.IsEnabled, Is.False);
            Assert.That(disabled.Version, Is.EqualTo(2));
        });

        var enabled = await service.SetEnabledAsync(entity.Id, true);

        Assert.Multiple(() =>
        {
            Assert.That(enabled, Is.Not.Null);
            Assert.That(enabled!.IsEnabled, Is.True);
            Assert.That(enabled.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Create_DuplicateKey_ReturnsCleanValidationError()
    {
        await using var dbContext = CreateDbContext();
        await SeedGuidanceDocumentAsync(dbContext, key: "duplicate-key");
        var controller = CreateController(dbContext);

        var result = await controller.Create(new UpsertAiGuidanceRequest
        {
            Key = "duplicate-key",
            Title = "Duplicate",
            Surface = "studio",
            DocumentKind = AiGuidanceDocumentKinds.Guidance,
            OutputPath = ".automaton/agents/references/llms/duplicate-key.md",
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
                Title = "Studio A",
                Surface = "studio",
                DocumentKind = AiGuidanceDocumentKinds.Guidance,
                TagsJson = """["editor"]""",
                OutputPath = ".automaton/agents/references/llms/studio-a.md",
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
                Title = "Studio B",
                Surface = "studio",
                DocumentKind = AiGuidanceDocumentKinds.Guidance,
                TagsJson = """["editor"]""",
                OutputPath = ".automaton/agents/references/llms/studio-b-disabled.md",
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
                Title = "CLI A",
                Surface = "cli",
                DocumentKind = AiGuidanceDocumentKinds.Guidance,
                TagsJson = """["deploy"]""",
                OutputPath = ".automaton/agents/references/llms/cli-a.md",
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
    public async Task GetGuidanceByKeyAsync_ReturnsDatabaseDocument_WhenEnabledAndModelLoadable()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AiGuidanceDocuments.Add(new AiGuidanceDocument
        {
            Id = Guid.NewGuid(),
            Key = "automaton-studio-editor",
            Title = "Automaton Studio Editor",
            Surface = "studio",
            DocumentKind = AiGuidanceDocumentKinds.Guidance,
            TagsJson = """["editor"]""",
            OutputPath = ".automaton/agents/references/llms/automaton-studio-editor.md",
            ContentMarkdown = "# Database Override",
            IsEnabled = true,
            IsModelLoadable = true,
            SortOrder = 100,
            Version = 7,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var runtimeService = CreateRuntimeService(dbContext);
        var document = await runtimeService.GetGuidanceByKeyAsync("automaton-studio-editor");

        Assert.That(document, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(document!.IsDatabaseDocument, Is.True);
            Assert.That(document.Version, Is.EqualTo(7));
            Assert.That(document.ContentMarkdown, Is.EqualTo("# Database Override"));
        });
    }

    [Test]
    public async Task GetGuidanceByKeyAsync_ReturnsNull_WhenDatabaseDocMissing()
    {
        await using var dbContext = CreateDbContext();
        var runtimeService = CreateRuntimeService(dbContext);

        var document = await runtimeService.GetGuidanceByKeyAsync("automaton-studio-editor");

        Assert.That(document, Is.Null);
    }

    [Test]
    public async Task SeedMissingDefaultsAsync_ReconcilesExistingSeedDocument_AndPreservesEnabledStatus()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AiGuidanceDocuments.Add(new AiGuidanceDocument
        {
            Id = Guid.NewGuid(), Key = "automaton-actions", Title = "Old", Surface = "shared",
            DocumentKind = AiGuidanceDocumentKinds.Guidance, ContentMarkdown = "old", IsEnabled = false,
            IsModelLoadable = true, Version = 1, CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateRuntimeService(dbContext);
        await service.SeedMissingDefaultsAsync();

        var document = await dbContext.AiGuidanceDocuments.SingleAsync(item => item.Key == "automaton-actions");
        Assert.Multiple(() =>
        {
            Assert.That(document.Source, Is.EqualTo("Seed"));
            Assert.That(document.ContentMarkdown, Does.Contain("Actions are executable modules"));
            Assert.That(document.IsEnabled, Is.False);
            Assert.That(document.SeedContentHash, Is.Not.Null.And.Not.Empty);
            Assert.That(document.LastSeedSynchronizedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task SeedMissingDefaultsAsync_IsIdempotentAfterFirstSynchronization()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateRuntimeService(dbContext);

        Assert.That(await service.SeedMissingDefaultsAsync(), Is.GreaterThan(0));
        var before = await dbContext.AiGuidanceDocuments.SingleAsync(item => item.Key == "automaton-actions");
        var version = before.Version;
        var synchronizedAt = before.LastSeedSynchronizedAt;

        Assert.That(await service.SeedMissingDefaultsAsync(), Is.EqualTo(0));
        var after = await dbContext.AiGuidanceDocuments.SingleAsync(item => item.Key == "automaton-actions");
        Assert.Multiple(() =>
        {
            Assert.That(after.Version, Is.EqualTo(version));
            Assert.That(after.LastSeedSynchronizedAt, Is.EqualTo(synchronizedAt));
        });
    }

    [Test]
    public async Task DeleteAsync_RejectsSeedManagedDocument()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateAdminService(dbContext);
        await CreateRuntimeService(dbContext).SeedMissingDefaultsAsync();
        var entity = await dbContext.AiGuidanceDocuments.SingleAsync(item => item.Key == "automaton-actions");

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(entity.Id));
        Assert.That(error!.Message, Does.Contain("cannot be deleted"));
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
        string title = "Seed Title",
        string contentMarkdown = "# Seed",
        bool isEnabled = true)
    {
        var entity = new AiGuidanceDocument
        {
            Id = Guid.NewGuid(),
            Key = key,
            Title = title,
            Surface = "studio",
            DocumentKind = AiGuidanceDocumentKinds.Guidance,
            TagsJson = """["editor"]""",
            OutputPath = ".automaton/agents/references/llms/seed-key.md",
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
