using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Automaton.Studio.Services.AI;

/// <summary>
/// OpenAI implementation for AI step generation using the official OpenAI .NET library
/// </summary>
public class OpenAiStepGenerationProvider : IAiStepGenerationProvider
{
    private readonly IConfiguration configuration;
    private readonly ILogger<OpenAiStepGenerationProvider> logger;

    private const string DefaultModel = "gpt-4o-mini";

    public string ProviderName => "OpenAI";

    public OpenAiStepGenerationProvider(
        IConfiguration configuration,
        ILogger<OpenAiStepGenerationProvider> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task<AiGeneratedStepResult> GenerateCustomStepAsync(
        AiStepGenerationRequest request,
        IProgress<AiGenerationProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return CreateErrorResult("OpenAI API key not configured. Please set the OPENAI_API_KEY configuration.");
            }

            progressCallback?.Report(new AiGenerationProgress
            {
                Stage = "Initializing",
                Message = "Connecting to OpenAI...",
                PercentageComplete = 10
            });

            // Create OpenAI client
            var client = new OpenAIClient(apiKey);
            var chatClient = client.GetChatClient(configuration["OpenAI:Model"] ?? DefaultModel);

            var prompt = BuildPrompt(request);

            progressCallback?.Report(new AiGenerationProgress
            {
                Stage = "Generating",
                Message = "AI is analyzing your request and generating the custom step...",
                PercentageComplete = 30
            });

            // Create chat completion request
            var messages = new ChatMessage[]
            {
                new UserChatMessage(prompt)
            };

            var completionOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 2000,
                Temperature = 0.7f
            };

            var response = await chatClient.CompleteChatAsync(messages, completionOptions, cancellationToken);

            progressCallback?.Report(new AiGenerationProgress
            {
                Stage = "Processing",
                Message = "Processing AI response...",
                PercentageComplete = 80
            });

            // Extract the response content more safely
            string responseContent;
            if (response?.Value?.Content?.Count > 0)
            {
                responseContent = response.Value.Content[0].Text;
                logger.LogDebug("OpenAI response received with {ContentParts} content parts", response.Value.Content.Count);
            }
            else
            {
                logger.LogWarning("OpenAI response was empty or had no content parts");
                return CreateErrorResult("OpenAI returned an empty response");
            }

            var result = ParseOpenAiResponse(responseContent, request.Language);

            progressCallback?.Report(new AiGenerationProgress
            {
                Stage = "Complete",
                Message = "Custom step generated successfully!",
                PercentageComplete = 100,
                IsComplete = true
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("AI step generation was cancelled");
            return CreateErrorResult("Generation was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate custom step using OpenAI");
            return CreateErrorResult($"Failed to generate step: {ex.Message}");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return false;

            // Simple test request to verify API key
            var client = new OpenAIClient(apiKey);
            var chatClient = client.GetChatClient(configuration["OpenAI:Model"] ?? DefaultModel);

            var messages = new ChatMessage[]
            {
                new UserChatMessage("Test connection")
            };

            var completionOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 10
            };

            var response = await chatClient.CompleteChatAsync(messages, completionOptions, cancellationToken);
            return response != null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OpenAI availability check failed");
            return false;
        }
    }

    private string? GetApiKey()
    {
        // Check in order of preference:
        // 1. Runtime environment variable (set by the API)
        // 2. Configuration (appsettings)
        // 3. System environment variable
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
               configuration["OpenAI:ApiKey"] ?? 
               configuration["OPENAI_API_KEY"];
    }

    private string BuildPrompt(AiStepGenerationRequest request)
    {
        var promptBuilder = new StringBuilder();

        promptBuilder.AppendLine("You are an expert programmer creating custom automation steps. Generate a custom step based on the following requirements:");
        promptBuilder.AppendLine($"Description: {request.Description}");
        promptBuilder.AppendLine($"Programming Language: {request.Language}");
        
        if (!string.IsNullOrEmpty(request.Context))
        {
            promptBuilder.AppendLine($"Additional Context: {request.Context}");
        }

        if (request.ExpectedInputs?.Any() == true)
        {
            promptBuilder.AppendLine($"Expected Inputs: {string.Join(", ", request.ExpectedInputs)}");
        }

        if (request.ExpectedOutputs?.Any() == true)
        {
            promptBuilder.AppendLine($"Expected Outputs: {string.Join(", ", request.ExpectedOutputs)}");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Requirements:");
        promptBuilder.AppendLine("1. Generate clean, production-ready code");
        promptBuilder.AppendLine("2. Include proper error handling");
        promptBuilder.AppendLine("3. Add helpful comments explaining the logic");
        promptBuilder.AppendLine("4. Define clear input and output variables");
        promptBuilder.AppendLine("5. Follow best practices for the specified language");
        
        if (request.Language.Equals("Python", StringComparison.OrdinalIgnoreCase))
        {
            promptBuilder.AppendLine("6. Use appropriate Python libraries when needed");
            promptBuilder.AppendLine("7. Include proper variable assignments for outputs");
        }
        else if (request.Language.Equals("Batch", StringComparison.OrdinalIgnoreCase))
        {
            promptBuilder.AppendLine("6. Use Windows batch scripting best practices");
            promptBuilder.AppendLine("7. Use environment variables for input/output");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("IMPORTANT JSON FORMATTING RULES:");
        promptBuilder.AppendLine("- All backslashes in code must be escaped as double backslashes");
        promptBuilder.AppendLine("- All quotes in code must be escaped with backslash");
        promptBuilder.AppendLine("- All newlines in code must be escaped as backslash-n");
        promptBuilder.AppendLine("- The code field must be properly JSON-escaped");
        promptBuilder.AppendLine();
        
        promptBuilder.AppendLine("Return your response as a JSON object with the following structure:");
        
        // Build the JSON template without escaping issues
        var jsonTemplate = @"{
    ""stepName"": ""CamelCaseStepName"",
    ""displayName"": ""Human Readable Step Name"",
    ""description"": ""Brief description of what this step does"",
    ""code"": ""// The actual code here with proper JSON escaping"",
    ""inputVariables"": [
        {
            ""name"": ""variableName"",
            ""type"": ""String|Number|Boolean"",
            ""description"": ""Variable description"",
            ""isRequired"": true,
            ""defaultValue"": """"
        }
    ],
    ""outputVariables"": [
        {
            ""name"": ""resultVariableName"",
            ""type"": ""String|Number|Boolean"",
            ""description"": ""Output description""
        }
    ]
}";
        
        promptBuilder.AppendLine(jsonTemplate);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Generate ONLY valid JSON with properly escaped strings. No additional text, explanations, or markdown formatting.");

        return promptBuilder.ToString();
    }

    private AiGeneratedStepResult ParseOpenAiResponse(string responseContent, string language)
    {
        try
        {
            if (string.IsNullOrEmpty(responseContent))
            {
                return CreateErrorResult("Empty response from OpenAI");
            }

            // Log the raw response for debugging
            logger.LogDebug("Raw OpenAI response: {Response}", responseContent);

            // More comprehensive cleanup for various markdown formats
            var content = responseContent.Trim();
            
            // Remove markdown code blocks (```json ... ``` or ``` ... ```)
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                if (lines.Length > 2)
                {
                    // Remove first and last lines if they contain markdown
                    var firstLine = lines[0].Trim();
                    var lastLine = lines[^1].Trim();
                    
                    if ((firstLine.StartsWith("```json") || firstLine == "```") && 
                        (lastLine == "```" || lastLine.StartsWith("```")))
                    {
                        content = string.Join("\n", lines[1..^1]).Trim();
                    }
                }
            }

            // Additional cleanup
            content = content
                .Replace("```json", "")
                .Replace("```", "")
                .Trim()
                .Trim('`');

            // Validate that we have JSON-like content
            if (!content.StartsWith("{") || !content.EndsWith("}"))
            {
                logger.LogWarning("Response content doesn't appear to be valid JSON: {Content}", content);
                return CreateErrorResult($"Invalid JSON response format. Expected JSON object but got: {content.Substring(0, Math.Min(100, content.Length))}...");
            }

            logger.LogDebug("Cleaned content for JSON parsing: {Content}", content);

            // Try parsing with standard approach first
            var stepDefinition = TryParseJson(content);
            
            if (stepDefinition == null)
            {
                // If standard parsing failed, try to fix common escaping issues
                logger.LogDebug("Standard JSON parsing failed, attempting to fix escaping issues");
                var fixedContent = FixJsonEscaping(content);
                logger.LogDebug("Fixed JSON content: {Content}", fixedContent);
                stepDefinition = TryParseJson(fixedContent);
            }

            if (stepDefinition == null)
            {
                return CreateErrorResult("Failed to parse AI response - JSON parsing failed even after attempting fixes");
            }

            return new AiGeneratedStepResult
            {
                IsSuccess = true,
                StepName = stepDefinition.StepName ?? "GeneratedStep",
                DisplayName = stepDefinition.DisplayName ?? "Generated Step",
                Description = stepDefinition.Description ?? "AI-generated custom step",
                Code = stepDefinition.Code ?? "",
                Language = language,
                InputVariables = stepDefinition.InputVariables?.Select(v => new AiGeneratedVariable
                {
                    Name = v.Name ?? "",
                    Type = v.Type ?? "String",
                    Description = v.Description,
                    IsRequired = v.IsRequired,
                    DefaultValue = v.DefaultValue
                }).ToList() ?? new List<AiGeneratedVariable>(),
                OutputVariables = stepDefinition.OutputVariables?.Select(v => new AiGeneratedVariable
                {
                    Name = v.Name ?? "",
                    Type = v.Type ?? "String",
                    Description = v.Description
                }).ToList() ?? new List<AiGeneratedVariable>()
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse OpenAI response as JSON. Content: {Content}", responseContent);
            return CreateErrorResult($"Failed to parse AI response: {ex.Message}. Response was: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error parsing OpenAI response");
            return CreateErrorResult($"Unexpected error parsing response: {ex.Message}");
        }
    }

    private OpenAiStepDefinition? TryParseJson(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<OpenAiStepDefinition>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "JSON parsing attempt failed");
            return null;
        }
    }

    private string FixJsonEscaping(string content)
    {
        try
        {
            // Parse as JsonDocument to find the structure
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            
            // If we get here, the JSON is actually valid, so return as-is
            return content;
        }
        catch (JsonException)
        {
            // The JSON is malformed, try to fix common issues
            logger.LogDebug("Attempting to fix JSON escaping issues");
            
            // Find the "code" field and fix escaping issues within it
            var codeFieldMatch = System.Text.RegularExpressions.Regex.Match(content, 
                @"""code""\s*:\s*""((?:[^""\\]|\\.)*)""", 
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (codeFieldMatch.Success)
            {
                var codeValue = codeFieldMatch.Groups[1].Value;
                
                // Fix common escaping issues in the code field
                var fixedCodeValue = codeValue
                    .Replace("\\", "\\\\")  // Fix single backslashes
                    .Replace("\"", "\\\"")  // Fix unescaped quotes
                    .Replace("\r\n", "\\n") // Fix actual newlines
                    .Replace("\n", "\\n")   // Fix remaining newlines
                    .Replace("\r", "\\r")   // Fix carriage returns
                    .Replace("\t", "\\t");  // Fix tabs
                
                // Replace in the original content
                var fixedContent = content.Replace(
                    codeFieldMatch.Value, 
                    $"\"code\": \"{fixedCodeValue}\"");
                
                logger.LogDebug("Applied escaping fixes to code field");
                return fixedContent;
            }
            
            // If we can't find a code field, return the original
            return content;
        }
    }

    private static AiGeneratedStepResult CreateErrorResult(string errorMessage)
    {
        return new AiGeneratedStepResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    // Internal models for JSON deserialization
    private class OpenAiStepDefinition
    {
        public string? StepName { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? Code { get; set; }
        public OpenAiVariable[]? InputVariables { get; set; }
        public OpenAiVariable[]? OutputVariables { get; set; }
    }

    private class OpenAiVariable
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Description { get; set; }
        public bool IsRequired { get; set; }
        public string? DefaultValue { get; set; }
    }
}