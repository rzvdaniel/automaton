namespace Automaton.Runner.Console.Models;

/// <summary>
/// Result returned to the server after handling an UploadControllerScript request.
/// When a requirements.txt is installed, Logs contains the pip install output lines.
/// </summary>
public class AppControllerUploadResult
{
    public bool Success { get; set; }
    public List<string>? Logs { get; set; }
}
