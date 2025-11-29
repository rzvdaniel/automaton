namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links sensitive variables to users for authorization
/// </summary>
public class SensitiveVariableUser
{
    public Guid SensitiveVariableId { get; set; }
    public Guid UserId { get; set; }
}
