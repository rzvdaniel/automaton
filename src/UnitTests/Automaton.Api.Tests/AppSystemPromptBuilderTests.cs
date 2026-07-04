using Automaton.AiGuidance;
using Automaton.Mcp.SystemPrompts;
using Automaton.Mcp.Tools;
using System.Text.Json;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppSystemPromptBuilderTests
{
    [Test]
    public async Task BuildSystemPrompt_UsesCompactStudioContract_AndExcludesCliBootstrapPhrases()
    {
        var builder = new AppSystemPromptBuilder(
            new EmbeddedAutomatonGuidanceRuntimeProvider(new EmbeddedAutomatonGuidanceProvider()),
            new KeywordAppPromptContextSelector(),
            new StubStepSystemPromptBuilder(),
            [new StubVerificationStrategy()],
            [new StubExecutionPolicy()]);

        var prompt = await builder.BuildSystemPromptAsync(
            ComplexityTier.Medium,
            AppVerificationStrategyNames.Balanced,
            AppExecutionPolicyNames.Default,
            "Tighten the dashboard layout.");

        Assert.That(prompt, Does.Contain("# Automaton App Contract (Compact)"));
        Assert.That(prompt, Does.Contain("frontend/dist"));
        Assert.That(prompt, Does.Contain("AUTOMATON_APP_DATA_DIR"));
        Assert.That(prompt, Does.Contain("# Automaton Studio App Editor"));

        Assert.That(prompt, Does.Not.Contain("automaton login"));
        Assert.That(prompt, Does.Not.Contain("automaton new"));
        Assert.That(prompt, Does.Not.Contain("## AI shortest path"));
        Assert.That(prompt, Does.Not.Contain("## Full-stack todo example"));
    }

    [Test]
    public async Task BuildSystemPrompt_IncludesFocusedBackendGuidance_WhenRequestNeedsPersistence()
    {
        var builder = new AppSystemPromptBuilder(
            new EmbeddedAutomatonGuidanceRuntimeProvider(new EmbeddedAutomatonGuidanceProvider()),
            new KeywordAppPromptContextSelector(),
            new StubStepSystemPromptBuilder(),
            [new StubVerificationStrategy()],
            [new StubExecutionPolicy()]);

        var prompt = await builder.BuildSystemPromptAsync(
            ComplexityTier.Medium,
            AppVerificationStrategyNames.Balanced,
            AppExecutionPolicyNames.Default,
            "Add a backend controller with sqlite persistence for todos.");

        Assert.That(prompt, Does.Contain("# Automaton Backend Controllers"));
    }

    [Test]
    public void ContextSelector_AddsFrontendAndStarterGuidance_ForNewAppRequests()
    {
        var selector = new KeywordAppPromptContextSelector();

        var references = selector.SelectAdditionalReferences("Create a new app from scratch with an installable PWA.");

        Assert.That(references, Does.Contain("automaton-frontend-structure"));
        Assert.That(references, Does.Contain("automaton-starter-prompts"));
    }

    [Test]
    public void ContextSelector_DoesNotTreatGenericApiMention_AsBackendWork()
    {
        var selector = new KeywordAppPromptContextSelector();

        var references = selector.SelectAdditionalReferences("Use the browser API and the OpenAI API from the frontend.");

        Assert.That(references, Does.Not.Contain("automaton-backend-controllers"));
    }

    [Test]
    public void ContextSelector_AddsDeployGuidance_ForPublishedAppFailures()
    {
        var selector = new KeywordAppPromptContextSelector();

        var references = selector.SelectAdditionalReferences("The published app shows a blank screen and a 404 after publishing.");

        Assert.That(references, Does.Contain("automaton-cli-deploy"));
    }

    [Test]
    public async Task AutomatonGuidanceTool_ListsFocusedGuidanceReferences()
    {
        var tool = new AutomatonGuidanceTool(new EmbeddedAutomatonGuidanceRuntimeProvider(new EmbeddedAutomatonGuidanceProvider()));

        var result = await tool.ExecuteToolAsync(
            AutomatonGuidanceTool.ListGuidanceName,
            JsonDocument.Parse("{}").RootElement,
            null);

        Assert.That(result.Success, Is.True);
        using var json = JsonDocument.Parse(result.Content!);
        var references = json.RootElement.GetProperty("references").EnumerateArray().ToList();
        var studioEditor = references.Single(item => item.GetProperty("key").GetString() == "automaton-studio-editor");

        Assert.That(references.Any(item => item.GetProperty("key").GetString() == "automaton-backend-controllers"), Is.True);
        Assert.That(studioEditor.GetProperty("surface").GetString(), Is.EqualTo("studio"));
        Assert.That(studioEditor.GetProperty("documentKind").GetString(), Is.EqualTo("Guidance"));
        Assert.That(studioEditor.GetProperty("description").GetString(), Does.Contain("Studio editor"));
    }

    [Test]
    public async Task AutomatonGuidanceTool_ReturnsRequestedGuidanceContent()
    {
        var tool = new AutomatonGuidanceTool(new EmbeddedAutomatonGuidanceRuntimeProvider(new EmbeddedAutomatonGuidanceProvider()));

        var result = await tool.ExecuteToolAsync(
            AutomatonGuidanceTool.GetGuidanceName,
            JsonDocument.Parse("""
                {
                  "key": "automaton-media-assets"
                }
                """).RootElement,
            null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Does.Contain("# Automaton Media and Static Asset Rules"));
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
