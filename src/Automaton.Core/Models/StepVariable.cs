namespace Automaton.Core.Models;

public class StepVariable
{
    public string? Id { get; set; }
    public required string Name { get; set; }
    public virtual string? Value { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Indicates if this variable contains sensitive data (API keys, passwords, etc.)
    /// When true:
    /// - In DATABASE: Value contains an encrypted reference: "${sensitive:guid}"
    /// - In UI/Runner: Value contains the actual decrypted value (displayed in password input for UI)
    /// </summary>
    public bool IsSensitive { get; set; } = false;
}
