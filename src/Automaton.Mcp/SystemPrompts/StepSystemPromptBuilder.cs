using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Service for building AI system prompts for Step code generation
/// </summary>
public class StepSystemPromptBuilder : IStepSystemPromptBuilder
{
    private const string PromptResourceName = "Automaton.Mcp.SystemPrompts.Assets.Step.StepSystemPrompt.md";
    private static readonly Lazy<string> CachedPrompt = new(LoadPrompt, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Build a system prompt for Step code generation
    /// </summary>
    public string BuildSystemPrompt(string language)
    {
        var prompt = CachedPrompt.Value.Replace("{language}", language);

        // Add language-specific guidance
        if (string.Equals(language, "Python", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            ## PYTHON
            - Access inputs by name; for optional: globals().get('VarName', default)
            - No if __name__ == '__main__':; use top-level script
            - Assign outputs as plain variables
            - Log: print("INFO: ..."), print("WARN: ..."), print("ERROR: ...", file=sys.stderr)
            - Import sys if using stderr
            """;
        }
        else if (string.Equals(language, "PowerShell", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            ## POWERSHELL
            - Read inputs as defined variables
            - Initialize outputs upfront
            - Log: Write-Host "INFO: ...", Write-Warning "WARN: ...", Write-Error "ERROR: ..."
            - Avoid throwing exceptions
            """;
        }
        else if (string.Equals(language, "JavaScript", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            ## JAVASCRIPT
            - Access inputs from globals
            - Initialize outputs and assign on success
            - Log: console.log("INFO: ..."), console.warn("WARN: ..."), console.error("ERROR: ...")
            - Avoid throwing uncaught errors
            """;
        }

        prompt += """

            ## TOOLS
            - get_current_code: Retrieve existing code (use first for modifications)
            - get_step_variables: Get variable definitions
            - suggest_variables: Create input/output variables
            - save_code: Save code (call multiple times: save early, test, fix, save again)
            - execute_code: Test execution (retry until Success: true)
            """.Trim();

        return prompt;
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(StepSystemPromptBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{PromptResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
