using Automaton.AiGuidance;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Service for building AI system prompts for app generation, with tier-based template selection.
/// </summary>
public class AppSystemPromptBuilder : IAppSystemPromptBuilder
{
    private const string VerificationStrategyEnvVar = "AUTOMATON_APP_VERIFICATION_STRATEGY";
    private const string ExecutionPolicyEnvVar = "AUTOMATON_APP_EXECUTION_POLICY";

    private static readonly Lazy<string> SimplePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.Assets.App.app-simple.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> MediumPrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.Assets.App.app-medium.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> LargePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.Assets.App.app-large.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> EnterprisePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.Assets.App.app-enterprise.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> PlanningPrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.Assets.App.app-planning.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IAutomatonGuidanceProvider _guidanceProvider;
    private readonly IAppPromptContextSelector _promptContextSelector;
    private readonly IStepSystemPromptBuilder _stepPromptBuilder;
    private readonly IReadOnlyDictionary<string, IAppVerificationPromptStrategy> _verificationStrategies;
    private readonly IReadOnlyDictionary<string, IAppExecutionPolicyPromptStrategy> _executionPolicies;

    public AppSystemPromptBuilder(
        IAutomatonGuidanceProvider guidanceProvider,
        IAppPromptContextSelector promptContextSelector,
        IStepSystemPromptBuilder stepPromptBuilder,
        IEnumerable<IAppVerificationPromptStrategy> verificationStrategies,
        IEnumerable<IAppExecutionPolicyPromptStrategy> executionPolicies)
    {
        _guidanceProvider = guidanceProvider;
        _promptContextSelector = promptContextSelector;
        _stepPromptBuilder = stepPromptBuilder;
        _verificationStrategies = verificationStrategies
            .GroupBy(strategy => strategy.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _executionPolicies = executionPolicies
            .GroupBy(policy => policy.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build a system prompt for the given complexity tier.
    /// </summary>
    public string BuildSystemPrompt(
        ComplexityTier? tier = null,
        string? verificationStrategyName = null,
        string? executionPolicyName = null,
        string? userMessage = null)
    {
        var tierPrompt = tier switch
        {
            ComplexityTier.Simple     => SimplePrompt.Value,
            ComplexityTier.Medium     => MediumPrompt.Value,
            ComplexityTier.Large      => LargePrompt.Value,
            ComplexityTier.Enterprise => EnterprisePrompt.Value,
            _                         => MediumPrompt.Value
        };

        var sharedContractBlock = BuildSharedContractBlock(userMessage, tier);
        var verificationBlock = ResolveVerificationPromptBlock(verificationStrategyName);
        var executionPolicyBlock = ResolveExecutionPolicyPromptBlock(executionPolicyName);
        var precedenceGuardBlock = BuildPrecedenceGuardBlock();
        return $"{sharedContractBlock}\n\n{tierPrompt}\n\n{precedenceGuardBlock}\n\n{verificationBlock}\n\n{executionPolicyBlock}";
    }

    /// <summary>
    /// Wraps <paramref name="baseSystemPrompt"/> with the planning-phase instruction block
    /// loaded from <c>app-planning.md</c>.
    /// </summary>
    public string BuildPlanningPhaseSystemPrompt(string baseSystemPrompt) =>
        $"{baseSystemPrompt}\n\n{PlanningPrompt.Value}";

    private string ResolveVerificationPromptBlock(string? preferredStrategyName)
    {
        var requested = string.IsNullOrWhiteSpace(preferredStrategyName)
            ? Environment.GetEnvironmentVariable(VerificationStrategyEnvVar)
            : preferredStrategyName;

        var resolvedName = string.IsNullOrWhiteSpace(requested)
            ? AppVerificationStrategyNames.Balanced
            : requested.Trim();

        if (_verificationStrategies.TryGetValue(resolvedName, out var strategy))
        {
            return strategy.BuildPromptBlock();
        }

        if (_verificationStrategies.TryGetValue(AppVerificationStrategyNames.Balanced, out var balanced))
        {
            return balanced.BuildPromptBlock();
        }

        return
            """
## VERIFICATION STRATEGY
- Use focused verification and ensure runtime errors are fixed before completion.
""";
    }

    private string ResolveExecutionPolicyPromptBlock(string? preferredPolicyName)
    {
        var requested = string.IsNullOrWhiteSpace(preferredPolicyName)
            ? Environment.GetEnvironmentVariable(ExecutionPolicyEnvVar)
            : preferredPolicyName;

        var resolvedName = string.IsNullOrWhiteSpace(requested)
            ? AppExecutionPolicyNames.Default
            : requested.Trim();

        if (_executionPolicies.TryGetValue(resolvedName, out var policy))
        {
            return policy.BuildPromptBlock();
        }

        if (_executionPolicies.TryGetValue(AppExecutionPolicyNames.Default, out var @default))
        {
            return @default.BuildPromptBlock();
        }

        return
            """
## EXECUTION POLICY
- Use tools intentionally and avoid unnecessary tool churn.
""";
    }

    private static string BuildPrecedenceGuardBlock() =>
        """
## INSTRUCTION PRECEDENCE
- Resolve conflicts by priority: explicit user request > Studio/editor operating rules > verification strategy block > execution policy block > shared Automaton app contract > tier guidance.
- Do not follow lower-priority guidance when it conflicts with higher-priority directives.
""";

    private string BuildSharedContractBlock(string? userMessage, ComplexityTier? tier)
    {
        var contract = _guidanceProvider.GetReference("llms/automaton-app-contract-compact.md").Trim();
        var studioAdapter = _guidanceProvider.GetReference("llms/automaton-studio-editor.md").Trim();
        var sections = new List<string> { contract, studioAdapter };

        foreach (var reference in _promptContextSelector.SelectAdditionalReferences(userMessage, tier))
        {
            sections.Add(_guidanceProvider.GetReference(reference).Trim());
        }

        return string.Join("\n\n", sections);
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AppSystemPromptBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
