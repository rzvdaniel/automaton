namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Service for building AI system prompts for Step code generation
/// </summary>
public class StepSystemPromptBuilder : IStepSystemPromptBuilder
{
    /// <summary>
    /// Build a system prompt for Step code generation
    /// </summary>
    public string BuildSystemPrompt(string language)
    {
        var prompt = $"""
            Generate {language} code for automation workflow steps. Follow this workflow:

            ## WORKFLOW
            1. For modifications: Call get_current_code first
            2. Check variables: Call get_step_variables
            3. Create variables if needed: Call suggest_variables (use lowercase_with_underscores naming)
            4. Generate code following rules below
            5. Save early: Call save_code to show progress
            6. Test: Call execute_code
            7. Fix errors: Modify code, save again, retest until Success: true
            8. Missing runtime inputs (API keys): Create input variables, generate code that uses them

            ## EXECUTION RULES
            - Input variables are injected as globals; read them directly by name
            - Output variables must be assigned at module/global scope with exact names
            - Initialize outputs with defaults at top; overwrite on success
            - Use JSON-serializable types only: string, number, boolean, list, dict
            - Avoid reserved names: sys, StringIO, _original_stdout, _original_stderr, _captured_output, _output_content
            - Log with prefixes: INFO:, WARN:, ERROR:
            - Catch exceptions, log ERROR, don't rethrow; preserve defaults

            ## CODE QUALITY
            - Proper error handling with try/catch blocks
            - All output variables assigned even on error
            - Test code before finalizing; fix all errors
            - Follow language best practices
            """.Trim();

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
}
