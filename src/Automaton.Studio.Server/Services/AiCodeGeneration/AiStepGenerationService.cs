using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services.AiProviders;
using Automaton.Studio.Server.Services.Mcp;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiCodeGeneration;

public class AiStepGenerationService
{
    private readonly AiApiKeyService aiApiKeyService;
    private readonly AiProviderFactory aiProviderFactory;
    private readonly McpService mcpService;
    private readonly ILogger<AiStepGenerationService> logger;

    public AiStepGenerationService(
        AiApiKeyService aiApiKeyService, 
        AiProviderFactory aiProviderFactory,
        McpService mcpService,
        ILogger<AiStepGenerationService> logger)
    {
        this.aiApiKeyService = aiApiKeyService;
        this.aiProviderFactory = aiProviderFactory;
        this.mcpService = mcpService;
        this.logger = logger;
    }

    public async Task<CodeGenerationResult> GenerateCodeAsync(Guid userId, StepGenerationRequest request)
    {
        try
        {
            // Get the API key for the specified provider
            var apiKey = await GetApiKeyForProviderAsync(userId, request.SelectedAiApiKeyId, request.Provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                return CodeGenerationResult.Failure("No valid API key found for the specified provider.");
            }

            // Generate the appropriate prompt
            var prompt = BuildPrompt(request);

            // Get MCP tools if enabled
            List<McpToolDefinition>? tools = null;
            List<McpToolCall>? toolCalls = null;
            if (request.EnableTools && request.Provider == AiProvider.OpenAI)
            {
                tools = mcpService.GetToolDefinitions();
                logger.LogInformation("Tools enabled for request. Found {ToolCount} tools: {ToolNames}", 
                    tools?.Count ?? 0, 
                    tools?.Any() == true ? string.Join(", ", tools.Select(t => t.Function?.Name)) : "none");
            }
            else
            {
                logger.LogInformation("Tools not enabled. EnableTools: {EnableTools}, Provider: {Provider}", 
                    request.EnableTools, request.Provider);
            }

            // Get the appropriate AI provider and call it
            var provider = aiProviderFactory.CreateProvider(request.Provider);
            var model = request.Model ?? provider.DefaultModel;
            
            string responseContent;
            if (provider is OpenAiProvider openAiProvider && tools?.Any() == true)
            {
                // Call with tools support
                responseContent = await openAiProvider.GenerateCodeAsync(apiKey, prompt, model, request.ConversationHistory, tools);
                
                // Parse the response to check for tool calls
                var responseJson = JsonDocument.Parse(responseContent);
                var choice = responseJson.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                
                // Check if there are tool calls
                if (message.TryGetProperty("tool_calls", out var toolCallsElement))
                {
                    toolCalls = new List<McpToolCall>();
                    foreach (var toolCall in toolCallsElement.EnumerateArray())
                    {
                        toolCalls.Add(new McpToolCall
                        {
                            Id = toolCall.GetProperty("id").GetString() ?? string.Empty,
                            Name = toolCall.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                            Arguments = toolCall.GetProperty("function").GetProperty("arguments").GetString() ?? string.Empty
                        });
                    }
                    
                    // Execute tool calls and return with tool execution info
                    var toolResults = new List<McpToolResult>();
                    foreach (var toolCall in toolCalls)
                    {
                        var result = await mcpService.ExecuteToolAsync(toolCall.Name, toolCall.Arguments, userId);
                        result.ToolCallId = toolCall.Id;
                        toolResults.Add(result);
                    }
                    
                    return CodeGenerationResult.SuccessWithTools(
                        message.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : string.Empty,
                        toolCalls,
                        toolResults);
                }
                
                // No tool calls, extract content
                if (message.TryGetProperty("content", out var contentProp))
                {
                    var generatedCode = contentProp.GetString() ?? string.Empty;
                    return CodeGenerationResult.Success(generatedCode);
                }
                
                return CodeGenerationResult.Failure("No content in AI response");
            }
            else
            {
                // Regular call without tools
                responseContent = await provider.GenerateCodeAsync(apiKey, prompt, model, request.ConversationHistory);
                return CodeGenerationResult.Success(responseContent);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating code for user {UserId} with provider {Provider}", userId, request.Provider);
            return CodeGenerationResult.Failure($"Code generation failed: {ex.Message}");
        }
    }

    private async Task<string?> GetApiKeyForProviderAsync(Guid userId, Guid? aiApiKeyId, AiProvider provider)
    {
        // If a specific key is selected, use that
        if (aiApiKeyId.HasValue)
        {
            return await aiApiKeyService.GetDecryptedApiKeyAsync(aiApiKeyId.Value, userId);
        }

        // Otherwise, try to find any available key for this provider
        var keys = await aiApiKeyService.GetAiApiKeysByProviderAsync(userId, provider);
        var firstKey = keys.FirstOrDefault();
        if (firstKey != null)
        {
            return await aiApiKeyService.GetDecryptedApiKeyAsync(firstKey.Id, userId);
        }

        // For desktop, check environment variables
        var envKeys = aiApiKeyService.GetEnvironmentApiKeys();
        if (envKeys.TryGetValue(provider, out var envKey) && !string.IsNullOrEmpty(envKey))
        {
            return envKey;
        }

        return null;
    }

    private string BuildPrompt(StepGenerationRequest request)
    {
        var prompt = $$"""
            You are an expert automation developer. Generate {request.Language} code for a custom step in an automation workflow.

            Step Description: {request.Description}

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

        // Add tool usage instructions if tools are enabled and available
        if (request.EnableTools && request.Provider == AiProvider.OpenAI)
        {
            prompt += """

            Tool Usage Guidelines:
            - When the user asks you to "execute", "run", "test", or "try" the step code, use the available tools to:
              1. First get the current step code using get_custom_step_code if needed
              2. Then execute the step using execute_custom_step to test it
            - When the user wants to see the current code in the editor, use get_custom_step_code
            - When modifying existing code, first retrieve the current code, then provide the updated version
            - Use tools proactively when they would be helpful for understanding the context or testing functionality
            """;
        }

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

        // Add current step context if available
        if (!string.IsNullOrEmpty(request.CurrentStepId))
        {
            prompt += $"\nCurrent Step Context: You are working on step ID {request.CurrentStepId}. You can use tools to get the current code or execute it for testing.";
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
}

public class StepGenerationRequest
{
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = "Python";
    public AiProvider Provider { get; set; }
    public Guid? SelectedAiApiKeyId { get; set; }
    public string? Model { get; set; }
    public List<VariableInfo>? InputVariables { get; set; }
    public List<VariableInfo>? OutputVariables { get; set; }
    public List<ConversationMessage>? ConversationHistory { get; set; }
    public bool EnableTools { get; set; } = true; // Enable MCP tools by default
    public string? CurrentStepId { get; set; } // For context when using tools
}

public class VariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CodeGenerationResult
{
    public bool IsSuccess { get; set; }
    public string GeneratedCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public List<McpToolCall>? ToolCalls { get; set; }
    public List<McpToolResult>? ToolResults { get; set; }

    public static CodeGenerationResult Success(string code) => new() { IsSuccess = true, GeneratedCode = code };
    public static CodeGenerationResult Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
    public static CodeGenerationResult SuccessWithTools(string code, List<McpToolCall> toolCalls, List<McpToolResult> toolResults) => 
        new() { IsSuccess = true, GeneratedCode = code, ToolCalls = toolCalls, ToolResults = toolResults };
}