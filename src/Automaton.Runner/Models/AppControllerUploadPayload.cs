namespace Automaton.Runner.Console.Models;

/// <summary>
/// Payload sent from server to runner when deploying a backend controller script.
/// Runner writes this to its local persistent volume so execution calls don't need to transfer code.
/// </summary>
public class AppControllerUploadPayload
{
    public Guid AppId { get; set; }
    public Guid DeploymentTargetId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string ControllerName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string Language { get; set; } = string.Empty;
}
