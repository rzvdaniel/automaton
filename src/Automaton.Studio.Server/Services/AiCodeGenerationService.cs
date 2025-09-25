using Automaton.Studio.Server.Entities;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class AiCodeGenerationService
{
    private readonly AiApiKeyService aiApiKeyService;
    private readonly HttpClient httpClient;
    private readonly ILogger<AiCodeGenerationService> logger;

    public AiCodeGenerationService(AiApiKeyService aiApiKeyService, HttpClient httpClient, ILogger<AiCodeGenerationService> logger)
    {
        this.aiApiKeyService = aiApiKeyService;
        this.httpClient = httpClient;
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

            // Call the appropriate AI service
            var generatedCode = request.Provider switch
            {
                AiProvider.OpenAI => await CallOpenAIAsync(apiKey, prompt, request.Model ?? "gpt-4"),
                AiProvider.Anthropic => await CallAnthropicAsync(apiKey, prompt, request.Model ?? "claude-3-sonnet"),
                AiProvider.Google => await CallGoogleAsync(apiKey, prompt, request.Model ?? "gemini-pro"),
                AiProvider.Azure => await CallAzureOpenAIAsync(apiKey, prompt, request.Model ?? "gpt-4"),
                _ => throw new NotSupportedException($"Provider {request.Provider} is not supported yet.")
            };

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

    private async Task<string> CallOpenAIAsync(string apiKey, string prompt, string model)
    {
        var request = new
        {
            model = model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 2000,
            temperature = 0.7
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseContent);
        
        return responseJson.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CallAnthropicAsync(string apiKey, string prompt, string model)
    {
        var request = new
        {
            model = model,
            max_tokens = 2000,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseContent);
        
        return responseJson.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CallGoogleAsync(string apiKey, string prompt, string model)
    {
        var request = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 2000,
                temperature = 0.7
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseContent);
        
        return responseJson.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CallAzureOpenAIAsync(string apiKey, string prompt, string model)
    {
        // Note: Azure OpenAI requires endpoint configuration
        // This is a simplified implementation - in production, you'd need endpoint configuration per key
        throw new NotImplementedException("Azure OpenAI integration requires endpoint configuration. Please implement based on your Azure setup.");
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