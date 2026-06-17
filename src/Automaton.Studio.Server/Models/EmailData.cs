namespace Automaton.Api.Models;

public class EmailData
{
    public required string Template { get; set; }
    public Dictionary<string, string> Variables { get; set; } = [];
    public required EmailAccount From { get; set; }
    public required IEnumerable<EmailAccount> To { get; set; } = [];
}
