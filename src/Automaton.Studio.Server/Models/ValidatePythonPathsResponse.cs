namespace Automaton.Studio.Server.Models;

/// <summary>
/// Response from Python path validation
/// </summary>
public class ValidatePythonPathsResponse
{
    public bool IsValid { get; set; }
    
    public List<PathValidationResult> Results { get; set; } = new();
    
    public string? Message { get; set; }
}

/// <summary>
/// Validation result for a single path
/// </summary>
public class PathValidationResult
{
    public string Path { get; set; } = string.Empty;
    
    public bool Exists { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public string? Type { get; set; } // "dll", "directory", "file"
}
