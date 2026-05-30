namespace Automaton.Studio.Server.Models;

public class UserDetails
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Email { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? PublicUsername { get; set; }

    /// <summary>
    /// User's system roles (e.g., "Admin", "SysAdmin", "User")
    /// </summary>
    public ICollection<string> Roles { get; set; } = new List<string>();
}
