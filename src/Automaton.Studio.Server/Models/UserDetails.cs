namespace Automaton.Studio.Server.Models;

public class UserDetails
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Email { get; set; }

    public string? PublicUsername { get; set; }
}
