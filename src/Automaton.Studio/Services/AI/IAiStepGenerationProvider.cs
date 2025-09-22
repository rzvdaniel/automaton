using Automaton.Studio.Domain;

namespace Automaton.Studio.Services.AI;

/// <summary>
/// Interface for AI providers that can generate custom steps
/// </summary>
public interface IAiStepGenerationProvider
{
    /// <summary>
    /// The name of the AI provider (e.g., "OpenAI", "Azure OpenAI", "Anthropic")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Generate a custom step based on a description
    /// </summary>
    /// <param name="request">The generation request containing description, language preferences, etc.</param>
    /// <param name="progressCallback">Optional callback for streaming progress updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The generated custom step definition</returns>
    Task<AiGeneratedStepResult> GenerateCustomStepAsync(
        AiStepGenerationRequest request,
        IProgress<AiGenerationProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the provider is properly configured and available
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request model for AI step generation
/// </summary>
public class AiStepGenerationRequest
{
    /// <summary>
    /// Natural language description of what the step should do
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Preferred programming language (Python, Batch, PowerShell, etc.)
    /// </summary>
    public string Language { get; set; } = "Python";

    /// <summary>
    /// Additional context or constraints for the generation
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Expected input variable hints
    /// </summary>
    public List<string>? ExpectedInputs { get; set; }

    /// <summary>
    /// Expected output variable hints
    /// </summary>
    public List<string>? ExpectedOutputs { get; set; }
}

/// <summary>
/// Result of AI step generation
/// </summary>
public class AiGeneratedStepResult
{
    /// <summary>
    /// Whether the generation was successful
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if generation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Generated step name
    /// </summary>
    public string? StepName { get; set; }

    /// <summary>
    /// Generated display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Generated description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Generated code
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Generated input variables
    /// </summary>
    public List<AiGeneratedVariable> InputVariables { get; set; } = new();

    /// <summary>
    /// Generated output variables
    /// </summary>
    public List<AiGeneratedVariable> OutputVariables { get; set; } = new();

    /// <summary>
    /// The programming language of the generated code
    /// </summary>
    public string Language { get; set; } = "Python";
}

/// <summary>
/// AI-generated variable definition
/// </summary>
public class AiGeneratedVariable
{
    /// <summary>
    /// Variable name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Variable type (String, Number, Boolean, etc.)
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Variable description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this variable is required
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Default value if any
    /// </summary>
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Progress update during AI generation
/// </summary>
public class AiGenerationProgress
{
    /// <summary>
    /// Current stage of generation
    /// </summary>
    public required string Stage { get; set; }

    /// <summary>
    /// Progress message
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int PercentageComplete { get; set; }

    /// <summary>
    /// Whether this is the final progress update
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Any intermediate content (useful for streaming)
    /// </summary>
    public string? IntermediateContent { get; set; }
}