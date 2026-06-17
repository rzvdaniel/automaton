using System;
using System.Collections.Generic;
using MediatR;

namespace Automaton.Api.Application.Queries
{
    public class UserRolesQuery : IRequest<IEnumerable<string>>
    {
        public Guid UserId { get; set; }
    }
}