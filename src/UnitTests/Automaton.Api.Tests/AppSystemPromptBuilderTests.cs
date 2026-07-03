using Automaton.AiGuidance;
using Automaton.Mcp.SystemPrompts;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppSystemPromptBuilderTests
{
    [Test]
    public void BuildSystemPrompt_UsesCompactStudioContract_AndExcludesCliBootstrapPhrases()
    {
        var builder = new AppSystemPromptBuilder(
            new EmbeddedAutomatonGuidanceProvider(),
            new StubStepSystemPromptBuilder(),
            [new StubVerificationStrategy()],
            [new StubExecutionPolicy()]);

        var prompt = builder.BuildSystemPrompt(
            ComplexityTier.Medium,
            AppVerificationStrategyNames.Balanced,
            AppExecutionPolicyNames.Default);

        Assert.That(prompt, Does.Contain("# Automaton App Contract (Compact)"));
        Assert.That(prompt, Does.Contain("frontend/dist"));
        Assert.That(prompt, Does.Contain("AUTOMATON_APP_DATA_DIR"));
        Assert.That(prompt, Does.Contain("# Automaton Studio App Editor"));

        Assert.That(prompt, Does.Not.Contain("automaton login"));
        Assert.That(prompt, Does.Not.Contain("automaton new"));
        Assert.That(prompt, Does.Not.Contain("## AI shortest path"));
        Assert.That(prompt, Does.Not.Contain("## Full-stack todo example"));
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
