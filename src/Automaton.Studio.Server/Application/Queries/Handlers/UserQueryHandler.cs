using AuthServer.Core.Queries;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Automaton.Studio.Server.Entities;

namespace AuthServer.Application.Queries.Handlers
{
    public class UserQueryHandler : IRequestHandler<UserQuery, UserDetails>
    {
        private readonly UsersService _userManagerService;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserQueryHandler(UsersService userManagerService, UserManager<ApplicationUser> userManager)
        {
            _userManagerService = userManagerService;
            _userManager = userManager;
        }

        public async Task<UserDetails> Handle(UserQuery request, CancellationToken cancellationToken)
        {
            var user = await _userManagerService.GetUserById(request.Id);

            // Get user's roles
            var roles = await _userManager.GetRolesAsync(user);

            var userDetails = new UserDetails
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                PublicUsername = user.PublicUsername,
                RegisteredAtUtc = user.RegisteredAtUtc,
                LastLoginAtUtc = user.LastLoginAtUtc,
                RegistrationProvider = user.RegistrationProvider,
                IsLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
                Roles = roles.ToList()
            };

            return userDetails;
        }
    }
}

