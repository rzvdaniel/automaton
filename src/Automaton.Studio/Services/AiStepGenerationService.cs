using Automaton.Studio.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services;

public class AiStepGenerationService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AiStepGenerationService> logger;

    public AiStepGenerationService(HttpClient httpClient, ILogger<AiStepGenerationService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<CodeGenerationResponse> GenerateStepCodeAsync(CodeGenerationRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/AiStepGeneration/generate-step-code", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<CodeGenerationResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result ?? new CodeGenerationResponse { IsSuccess = false, ErrorMessage = "Unknown error occurred" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating step code with AI");
            return new CodeGenerationResponse 
            { 
                IsSuccess = false, 
                ErrorMessage = $"Step code generation failed: {ex.Message}" 
            };
        }
    }
}