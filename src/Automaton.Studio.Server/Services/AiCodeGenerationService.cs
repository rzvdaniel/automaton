using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services.AiProviders;

namespace Automaton.Studio.Server.Services;

public class AiCodeGenerationService
{
    private readonly AiApiKeyService aiApiKeyService;
    private readonly AiProviderFactory aiProviderFactory;
    private readonly ILogger<AiCodeGenerationService> logger;

    public AiCodeGenerationService(
        AiApiKeyService aiApiKeyService, 
        AiProviderFactory aiProviderFactory,
        ILogger<AiCodeGenerationService> logger)
    {
        this.aiApiKeyService = aiApiKeyService;
        this.aiProviderFactory = aiProviderFactory;
        this.logger = logger;
    }

    public async Task<CodeGenerationResult> GenerateCodeAsync(Guid userId, CodeGenerationRequest request)
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

            // Get the appropriate AI provider and call it
            var provider = aiProviderFactory.CreateProvider(request.Provider);
            var model = request.Model ?? provider.DefaultModel;
            var generatedCode = await provider.GenerateCodeAsync(apiKey, prompt, model);

            return CodeGenerationResult.Success(generatedCode);
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

    private string BuildPrompt(CodeGenerationRequest request)
    {
        var prompt = $"""
            You are an expert automation developer. Generate {request.Language} code for a custom step in an automation workflow.

            Step Description: {request.Description}

            Requirements:
            - Generate clean, well-commented code
            - Use the specified language: {request.Language}
            - The code should be suitable for automation workflows
            - Include error handling where appropriate
            - Follow best practices for the chosen language

            """;

        if (request.InputVariables?.Any() == true)
        {
            prompt += "\nInput Variables:\n";
            foreach (var variable in request.InputVariables)
            {
                prompt += $"- {variable.Name} ({variable.Type}): {variable.Description}\n";
            }
        }

        if (request.OutputVariables?.Any() == true)
        {
            prompt += "\nExpected Output Variables:\n";
            foreach (var variable in request.OutputVariables)
            {
                prompt += $"- {variable.Name} ({variable.Type}): {variable.Description}\n";
            }
        }

        prompt += """

            Generate only the code, without any explanatory text or markdown formatting.
            The code should be ready to use directly in the automation step.
            """;

        return prompt;
    }
}

public class CodeGenerationRequest
{
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = "Python";
    public AiProvider Provider { get; set; }
    public Guid? SelectedAiApiKeyId { get; set; }
    public string? Model { get; set; }
    public List<VariableInfo>? InputVariables { get; set; }
    public List<VariableInfo>? OutputVariables { get; set; }
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

    public static CodeGenerationResult Success(string code) => new() { IsSuccess = true, GeneratedCode = code };
    public static CodeGenerationResult Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}