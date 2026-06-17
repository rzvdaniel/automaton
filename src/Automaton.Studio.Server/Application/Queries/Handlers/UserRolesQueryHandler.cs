using Automaton.Api.Application.Queries;
using Automaton.Api.Services;
using MediatR;

namespace Automaton.Api.Application.Queries.Handlers
{
    public class UserRolesQueryHandler : IRequestHandler<UserRolesQuery, IEnumerable<string>>
    {
        private readonly UsersService _userManagerService;

        public UserRolesQueryHandler(UsersService userManagerService)
        {
            _userManagerService = userManagerService;
        }

        public Task<IEnumerable<string>> Handle(UserRolesQuery request, CancellationToken cancellationToken)
        {
            return _userManagerService.GetRoles(request.UserId);
        }
    }
}