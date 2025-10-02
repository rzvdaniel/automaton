using Automaton.Studio.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Client;
using Automaton.Mcp.Client.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// AI Step Generation Service that uses MCP client directly for real-time conversation
/// </summary>
public class AiStepGenerationService
{
    private readonly IAutomatonMcpClient mcpClient;
    private readonly AiApiKeyService aiApiKeyService;
    private readonly ILogger<AiStepGenerationService> logger;

    public AiStepGenerationService(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        ILogger<AiStepGenerationService> logger)
    {
        this.mcpClient = mcpClient;
        this.aiApiKeyService = aiApiKeyService;
        this.logger = logger;
    }

    /// <summary>
    /// Generate step code using MCP client with real-time conversation
    /// </summary>
    public async Task<CodeGenerationResponse> GenerateStepCodeAsync(CodeGenerationRequest request, Guid userId, string? currentStepId = null)
    {
        try
        {
            logger.LogInformation("Generating step code for user {UserId} with provider {Provider}", userId, request.Provider);

            // Get API key for the provider
            var apiKey = await GetApiKeyForProviderAsync(userId, request.SelectedAiApiKeyId, request.Provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                return new CodeGenerationResponse 
                { 
                    IsSuccess = false, 
                    ErrorMessage = "No valid API key found for the specified provider." 
                };
            }

            // Build system prompt with iterative workflow instructions
            var systemPrompt = BuildSystemPrompt(request);

            // Create MCP conversation request
            var conversationRequest = new ConversationRequest
            {
                InitialMessage = request.Description,
                SystemPrompt = systemPrompt,
                Provider = MapToMcpProvider(request.Provider),
                ApiKey = apiKey,
                Model = request.Model,
                ConversationHistory = request.ConversationHistory?.Select(cm => 
                    new Automaton.Mcp.Client.Models.ConversationMessage(cm.Role, cm.Content)).ToList(),
                Context = new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["currentStepId"] = currentStepId ?? string.Empty,
                    ["language"] = request.Language,
                    ["inputVariables"] = request.InputVariables ?? new List<VariableInfo>(),
                    ["outputVariables"] = request.OutputVariables ?? new List<VariableInfo>()
                }
            };

            // Start MCP conversation
            var result = await mcpClient.StartConversationAsync(conversationRequest);

            if (result.IsSuccess)
            {
                return new CodeGenerationResponse
                {
                    IsSuccess = true,
                    GeneratedCode = result.GeneratedCode ?? ExtractCodeFromMessages(result.Messages)
                };
            }
            else
            {
                return new CodeGenerationResponse
                {
                    IsSuccess = false,
                    ErrorMessage = result.ErrorMessage ?? "Unknown error occurred during code generation"
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating step code for user {UserId} with provider {Provider}", userId, request.Provider);
            return new CodeGenerationResponse 
            { 
                IsSuccess = false, 
                ErrorMessage = $"Step code generation failed: {ex.Message}" 
            };
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility - routes to server
    /// </summary>
    [Obsolete("Use GenerateStepCodeAsync with userId parameter for MCP integration")]
    public async Task<CodeGenerationResponse> GenerateStepCodeAsync(CodeGenerationRequest request)
    {
        // For backward compatibility, we'll need to get the current user ID
        // This is a placeholder - in a real implementation, you'd get this from the current context
        logger.LogWarning("Using legacy GenerateStepCodeAsync method - consider updating to use MCP integration");
        
        // Try to use a default user ID or get from context
        var userId = Guid.Empty; // TODO: Get from current authentication context
        return await GenerateStepCodeAsync(request, userId);
    }

    private async Task<string?> GetApiKeyForProviderAsync(Guid userId, Guid? aiApiKeyId, Automaton.Studio.Models.AiProvider provider)
    {
        try
        {
            // If a specific key is selected, use that
            if (aiApiKeyId.HasValue)
            {
                var decryptedKey = await aiApiKeyService.GetDecryptedApiKeyAsync(aiApiKeyId.Value);
                if (!string.IsNullOrEmpty(decryptedKey))
                {
                    return decryptedKey;
                }
                logger.LogWarning("Failed to decrypt API key {ApiKeyId}", aiApiKeyId.Value);
            }

            // Otherwise, try to find any available key for this provider
            var availableKeys = await aiApiKeyService.GetAiApiKeysByProviderAsync(provider);
            var firstKey = availableKeys.FirstOrDefault();
            if (firstKey != null)
            {
                var decryptedKey = await aiApiKeyService.GetDecryptedApiKeyAsync(firstKey.Id);
                if (!string.IsNullOrEmpty(decryptedKey))
                {
                    return decryptedKey;
                }
            }

            // For desktop, check environment variables
            var envKeys = await aiApiKeyService.GetEnvironmentApiKeysAsync();
            if (envKeys.TryGetValue(provider, out var envKey) && !string.IsNullOrEmpty(envKey))
            {
                return envKey;
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting API key for provider {Provider} and user {UserId}", provider, userId);
            return null;
        }
    }

    private string BuildSystemPrompt(CodeGenerationRequest request)
    {
        var prompt = $"""
            You are an expert automation developer. Generate {request.Language} code for a custom step in an automation workflow.

            IMPORTANT WORKFLOW INSTRUCTIONS:
            When tools are available, follow this process:
            1. ALWAYS start by using get_custom_step_code to retrieve the current code from the step
            2. Analyze the user's request and the current code
            3. Generate or modify the code as needed
            4. Use execute_custom_step to test the code and check if it works correctly
            5. If the execution shows errors or issues, modify the code and test again
            6. Only stop when the code executes successfully or when you're confident it meets the requirements
            7. When finished, provide the final working code as your response

            Execution environment and integration rules:
            - The host injects all input variables as top-level globals before execution. Read them directly by name.
            - Do not rename input variables. Treat them as already defined; only use defaults if explicitly required.
            - You MUST assign the expected output variables by exactly the given names at module/global scope (not just returned from a function).
            - Initialize all expected output variables with sensible defaults at the top of the script, then overwrite them on success. Keep defaults if an error occurs.
            - Use only JSON-serializable primitive types for outputs: strings, numbers (int/float), booleans, lists of primitives/strings, or dicts with string keys and primitive values.
            - Avoid using or redefining reserved names used by the host: sys, StringIO, _original_stdout, _original_stderr, _captured_output, _output_content.
            - Do not include any explanatory text, comments about the environment, or markdown; return CODE ONLY.
            - Do not read from stdin or prompt the user; the inputs are provided by the host.
            - On Windows paths use raw strings (r'C\\...') or escaped backslashes.

            Logging and error reporting:
            - Use standardized log level prefixes so the host can parse streams: INFO: <message>, WARN: <message>, ERROR: <message>.
            - Catch exceptions, log an ERROR line, and do not rethrow; preserve default outputs.
            - Prefer language-native console APIs, but keep the prefix format consistent.

            Requirements:
            - Generate clean, robust code with proper error handling
            - Use the specified language: {request.Language}
            - The code should be suitable for automation workflows
            - Follow best practices for the chosen language
            - Ensure all declared output variables are assigned even on error (use sensible defaults like empty string, 0, false, empty list/dict, or None/null as appropriate)
            """;

        if (request.InputVariables?.Any() == true)
        {
            prompt += "\nInput Variables (pre-injected by host, read by exact name):\n";
            foreach (var variable in request.InputVariables)
            {
                prompt += $"- {variable.Name} ({variable.Type}): {variable.Description}\n";
            }
        }

        if (request.OutputVariables?.Any() == true)
        {
            prompt += "\nExpected Output Variables (assign by exact name at module/global scope):\n";
            foreach (var variable in request.OutputVariables)
            {
                prompt += $"- {variable.Name} ({variable.Type}): {variable.Description}\n";
            }
        }

        // Language-specific guidance
        if (string.Equals(request.Language, "Python", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            Python-specific guidance:
            - Access inputs by name. For optional values, use: value = globals().get('VarName', <default>)
            - Do not wrap the entire logic under if __name__ == '__main__':; just write top-level script.
            - Assign outputs as plain variables at the end of the script.
            - Log with prefixes using print(); send errors to stderr when possible: print(f"ERROR: ...", file=sys.stderr); print("INFO: ...") and print("WARN: ...").
            """;
        }
        else if (string.Equals(request.Language, "PowerShell", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            PowerShell-specific guidance:
            - Read inputs as already defined variables.
            - Initialize outputs upfront and set them at the end.
            - Use Write-Host "INFO: ..." for info, Write-Warning "WARN: ..." for warnings, Write-Error "ERROR: ..." for errors; avoid throwing.
            """;
        }
        else if (string.Equals(request.Language, "Bash", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            Bash-specific guidance:
            - Treat inputs as environment variables or pre-set shell variables.
            - Initialize outputs and export them if required by the host.
            - Use echo "INFO: ..." for info, echo "WARN: ..." for warnings, and echo "ERROR: ..." 1>&2 for errors; avoid exiting non-zero on handled errors.
            """;
        }
        else if (string.Equals(request.Language, "Batch", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            Windows Batch-specific guidance:
            - Treat inputs as already defined variables (%VarName%).
            - Initialize outputs and set them at the end with SET.
            - Use echo INFO: ... for info, echo WARN: ... for warnings, and echo ERROR: ... 1>&2 for errors; avoid EXIT /B non-zero on handled errors.
            """;
        }
        else if (string.Equals(request.Language, "JavaScript", StringComparison.OrdinalIgnoreCase) || string.Equals(request.Language, "Node.js", StringComparison.OrdinalIgnoreCase))
        {
            prompt += """

            JavaScript/Node.js-specific guidance:
            - Access inputs from provided globals.
            - Initialize outputs and assign them on success.
            - Use console.log("INFO: ..."), console.warn("WARN: ..."), console.error("ERROR: ..."); avoid throwing uncaught errors.
            """;
        }

        prompt += """

            Generate only the code, without any explanatory text or markdown formatting.
            The code should be ready to use directly in the automation step.
            """;

        return prompt;
    }

    private static Automaton.Mcp.Client.Models.AiProvider MapToMcpProvider(Automaton.Studio.Models.AiProvider provider)
    {
        return provider switch
        {
            Automaton.Studio.Models.AiProvider.OpenAI => Automaton.Mcp.Client.Models.AiProvider.OpenAI,
            Automaton.Studio.Models.AiProvider.Anthropic => Automaton.Mcp.Client.Models.AiProvider.Anthropic,
            Automaton.Studio.Models.AiProvider.Google => Automaton.Mcp.Client.Models.AiProvider.Google,
            Automaton.Studio.Models.AiProvider.Azure => Automaton.Mcp.Client.Models.AiProvider.Azure,
            Automaton.Studio.Models.AiProvider.Custom => Automaton.Mcp.Client.Models.AiProvider.Custom,
            _ => Automaton.Mcp.Client.Models.AiProvider.OpenAI
        };
    }

    private string ExtractCodeFromMessages(List<Automaton.Mcp.Client.Models.ConversationMessage> messages)
    {
        var lastAssistantMessage = messages.LastOrDefault(m => m.Role.ToLowerInvariant() == "assistant");
        return lastAssistantMessage?.Content ?? string.Empty;
    }
}