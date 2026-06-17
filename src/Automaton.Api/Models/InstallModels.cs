namespace Automaton.Api.Models;

public class SystemAdminConfigCommand
{
    public required string Email { get; set; }

    public required string Password { get; set; }

    public required string Name { get; set; }
}
