using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Models;

public class NewApp
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public Guid? ExecutionRunnerId { get; set; }
}
