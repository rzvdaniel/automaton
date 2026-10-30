namespace Automaton.Api.Models;

public class NewCustomStep
{
    [System.ComponentModel.DataAnnotations.Required]
    public string Name { get; set; } = string.Empty;
}
