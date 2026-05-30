using AuthServer.Core.Queries;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using MediatR;

namespace AuthServer.Application.Queries.Handlers
{
    public class FilterUserQueryHandler : IRequestHandler<FilterUserQuery, IEnumerable<UserDetails>>
    {
        private readonly UsersService _userManagerService;

        public FilterUserQueryHandler(UsersService userManagerService)
        {
            _userManagerService = userManagerService;
        }

        public async Task<IEnumerable<UserDetails>> Handle(FilterUserQuery request,
            CancellationToken cancellationToken)
        {
            var res = await _userManagerService.GetUsers(request.Name,
                request.Email, request.PageIndex, request.PageSize
            );

            return res.Select(user => new UserDetails
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                PublicUsername = user.PublicUsername,
                Roles = user.UserRoles
                    .Where(userRole => userRole.Role != null)
                    .Select(userRole => userRole.Role.Name)
                    .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                    .Cast<string>()
                    .ToList()
            });
        }
    }
}
