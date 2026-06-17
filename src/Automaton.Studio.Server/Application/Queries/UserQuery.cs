using Automaton.Api.Models;
using MediatR;

namespace Automaton.Api.Application.Queries
{
    public class UserQuery : IRequest<UserDetails>
    {
        public Guid Id { get; set; }
    }
}