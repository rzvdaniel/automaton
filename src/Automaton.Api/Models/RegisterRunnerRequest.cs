using MediatR;

namespace Automaton.Api.Models
{
    public class RegisterRunnerRequest : IRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; set; } = string.Empty;
    }
}
