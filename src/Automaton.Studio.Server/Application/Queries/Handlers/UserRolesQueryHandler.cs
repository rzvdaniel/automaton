using AuthServer.Core.Queries;
using Automaton.Studio.Server.Services;
using MediatR;

namespace AuthServer.Application.Queries.Handlers
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