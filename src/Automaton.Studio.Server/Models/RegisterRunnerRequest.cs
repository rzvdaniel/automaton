using MediatR;

namespace Automaton.Api.Models
{
    public class RegisterRunnerRequest : IRequest
    {
        public string Name { get; set; }
    }
}
