using Automaton.AiGuidance;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Mcp.SystemPrompts;
using Automaton.Mcp.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppSystemPromptBuilderTests
{
    [Test]
    public async Task BuildSystemPrompt_UsesGenericStudioContract_AndExcludesCliBootstrapPhrases()
    {
        await using var dbContext = CreateDbContext();
        var runtimeProvider = await CreateRuntimeProviderAsync(dbContext);
        var builder = new AppSystemPromptBuilder(
            runtimeProvider,
            new KeywordAppPromptContextSelector(),
            new StubStepSystemPromptBuilder(),
            [new StubVerificationStrategy()],
            [new StubExecutionPolicy()]);

        var prompt = await builder.BuildSystemPromptAsync(
            ComplexityTier.Medium,
            AppVerificationStrategyNames.Balanced,
            AppExecutionPolicyNames.Default,
            "Tighten the dashboard layout.");

        Assert.That(prompt, Does.Contain("# Automaton app contract"));
        Assert.That(prompt, Does.Contain("`build.command`"));
        Assert.That(prompt, Does.Contain("stores the app"));
        Assert.That(prompt, Does.Contain("# Automaton Studio app editor"));

        Assert.That(prompt, Does.Not.Contain("automaton login"));
        Assert.That(prompt, Does.Not.Contain("automaton new"));
        Assert.That(prompt, Does.Not.Contain("## AI shortest path"));
        Assert.That(prompt, Does.Not.Contain("## Full-stack todo example"));
    }

    [Test]
    public async Task BuildSystemPrompt_UsesTheGenericContract_WhenRequestNeedsPersistence()
    {
        await using var dbContext = CreateDbContext();
        var runtimeProvider = await CreateRuntimeProviderAsync(dbContext);
        var builder = new AppSystemPromptBuilder(
            runtimeProvider,
            new KeywordAppPromptContextSelector(),
            new StubStepSystemPromptBuilder(),
            [new StubVerificationStrategy()],
            [new StubExecutionPolicy()]);

        var prompt = await builder.BuildSystemPromptAsync(
            ComplexityTier.Medium,
            AppVerificationStrategyNames.Balanced,
            AppExecutionPolicyNames.Default,
            "Add a backend controller with sqlite persistence for todos.");

        Assert.That(prompt, Does.Not.Contain("automaton-backend-controllers"));
    }

    [Test]
    public void ContextSelector_DoesNotAddStackGuidance_ForNewAppRequests()
    {
        var selector = new KeywordAppPromptContextSelector();

        var references = selector.SelectAdditionalReferences("Create a new app from scratch with an installable PWA.");

        Assert.That(references, Is.Empty);
    }

    [Test]
    public void ContextSelector_DoesNotTreatGenericApiMention_AsBackendWork()
    {
        var selector = new KeywordAppPromptContextSelector();

        var references = selector.SelectAdditionalReferences("Use the browser API and the OpenAI API from the frontend.");

        Assert.That(references, Is.Empty);
    }

    [Test]
    public void ContextSelector_DoesNotAddRemovedDeployGuidance_ForPublishedAppFailures()
    {
        var selector = new KeywordAppPromptContextSelector();

        var references = selector.SelectAdditionalReferences("The published app shows a blank screen and a 404 after publishing.");

        Assert.That(references, Is.Empty);
    }

    [Test]
    public void ContextSelector_AddsActionsGuidance_ForBackgroundJobs()
    {
        var selector = new KeywordAppPromptContextSelector();

        Assert.That(selector.SelectAdditionalReferences("Create a scheduled background export job"), Does.Contain("automaton-actions"));
    }

    [Test]
    public async Task AutomatonGuidanceTool_ListsFocusedGuidanceReferences()
    {
        await using var dbContext = CreateDbContext();
        var tool = new AutomatonGuidanceTool(await CreateRuntimeProviderAsync(dbContext));

        var result = await tool.ExecuteToolAsync(
            AutomatonGuidanceTool.ListGuidanceName,
            JsonDocument.Parse("{}").RootElement,
            null);

        Assert.That(result.Success, Is.True);
        using var json = JsonDocument.Parse(result.Content!);
        var references = json.RootElement.GetProperty("references").EnumerateArray().ToList();
        var studioEditor = references.Single(item => item.GetProperty("key").GetString() == "automaton-studio-editor");

        Assert.That(references.Any(item => item.GetProperty("key").GetString() == "automaton-actions"), Is.True);
        Assert.That(studioEditor.GetProperty("surface").GetString(), Is.EqualTo("studio"));
        Assert.That(studioEditor.GetProperty("documentKind").GetString(), Is.EqualTo("Guidance"));
        Assert.That(studioEditor.GetProperty("description").GetString(), Does.Contain("Automaton Studio"));
    }

    [Test]
    public async Task AutomatonGuidanceTool_ReturnsRequestedGuidanceContent()
    {
        await using var dbContext = CreateDbContext();
        var tool = new AutomatonGuidanceTool(await CreateRuntimeProviderAsync(dbContext));

        var result = await tool.ExecuteToolAsync(
            AutomatonGuidanceTool.GetGuidanceName,
            JsonDocument.Parse("""
                {
                  "key": "automaton-actions"
                }
                """).RootElement,
            null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Does.Contain("# Automaton Actions"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<AiGuidanceRuntimeService> CreateRuntimeProviderAsync(ApplicationDbContext dbContext)
    {
        var service = new AiGuidanceRuntimeService(
            dbContext,
            new EmbeddedAutomatonGuidanceProvider(),
            NullLogger<AiGuidanceRuntimeService>.Instance);
        await service.SeedMissingDefaultsAsync();
        return service;
    }

    private sealed class StubStepSystemPromptBuilder : IStepSystemPromptBuilder
    {
        public string BuildSystemPrompt(string language) => $"step:{language}";
    }

    private sealed class StubVerificationStrategy : IAppVerificationPromptStrategy
    {
        public string Name => AppVerificationStrategyNames.Balanced;

        public string BuildPromptBlock() => "## VERIFICATION STRATEGY\n- Stub balanced verification.";
    }

    private sealed class StubExecutionPolicy : IAppExecutionPolicyPromptStrategy
    {
        public string Name => AppExecutionPolicyNames.Default;

        public string BuildPromptBlock() => "## EXECUTION POLICY\n- Stub default execution.";
    }
}
