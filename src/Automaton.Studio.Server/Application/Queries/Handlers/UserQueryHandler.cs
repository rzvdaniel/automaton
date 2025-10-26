using AuthServer.Core.Queries;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using MediatR;

namespace AuthServer.Application.Queries.Handlers
{
    public class UserQueryHandler : IRequestHandler<UserQuery, UserDetails>
    {
        private readonly UsersService _userManagerService;
        public UserQueryHandler(UsersService userManagerService)
        {
            _userManagerService = userManagerService;
        }

        public async Task<UserDetails> Handle(UserQuery request, CancellationToken cancellationToken)
        {
            var user = await _userManagerService.GetUserById(request.Id);

            var userDetails = new UserDetails
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email
            };

            return userDetails;
        }
    }
}
