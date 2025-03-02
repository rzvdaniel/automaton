namespace Automaton.App.Authentication.Models;

public class ResetUserPasswordModel
{
    public string Email { get; set; }
    public string Password { get; set; }
    public string Code { get; set; }
}
