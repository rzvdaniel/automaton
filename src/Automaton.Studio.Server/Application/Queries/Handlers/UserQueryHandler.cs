using AuthServer.Core.Queries;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Application.Queries.Handlers
{
    public class UserQueryHandler : IRequestHandler<UserQuery, UserDetails>
    {
        private readonly UsersService _userManagerService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;

        public UserQueryHandler(UsersService userManagerService, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
        {
            _userManagerService = userManagerService;
            _userManager = userManager;
            _db = db;
        }

        public async Task<UserDetails> Handle(UserQuery request, CancellationToken cancellationToken)
        {
            var user = await _userManagerService.GetUserById(request.Id);
            var registrationProviders = await _userManagerService.ResolveRegistrationProvidersAsync([user], cancellationToken);

            // Get user's roles
            var roles = await _userManager.GetRolesAsync(user);
            var appCount = await _db.WebAppUsers.CountAsync(item => item.UserId == user.Id, cancellationToken);
            var runnerCount = await _db.RunnerUsers.CountAsync(item => item.UserId == user.Id, cancellationToken);
            var apps = await _db.WebAppUsers
                .AsNoTracking()
                .Where(item => item.UserId == user.Id)
                .Join(
                    _db.WebApps.AsNoTracking(),
                    webAppUser => webAppUser.WebAppId,
                    webApp => webApp.Id,
                    (_, webApp) => new UserAppLink
                    {
                        Id = webApp.Id,
                        Name = webApp.Name,
                    })
                .OrderBy(app => app.Name)
                .ToListAsync(cancellationToken);
            var runners = await _db.RunnerUsers
                .AsNoTracking()
                .Where(item => item.UserId == user.Id)
                .Join(
                    _db.Runners.AsNoTracking(),
                    runnerUser => runnerUser.RunnerId,
                    runner => runner.Id,
                    (_, runner) => new UserRunnerLink
                    {
                        Id = runner.Id,
                        Name = runner.Name,
                        Status = RunnerPresenceEvaluator.DeriveStatus(runner.LastHeartbeatAtUtc, DateTime.UtcNow).ToString(),
                        LastHeartbeatAtUtc = runner.LastHeartbeatAtUtc,
                        LastConnectedAtUtc = runner.LastConnectedAtUtc,
                        Version = runner.Version,
                    })
                .OrderBy(runner => runner.Name)
                .ToListAsync(cancellationToken);

            var userDetails = new UserDetails
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                PublicUsername = user.PublicUsername,
                RegisteredAtUtc = user.RegisteredAtUtc,
                LastLoginAtUtc = user.LastLoginAtUtc,
                RegistrationProvider = registrationProviders.GetValueOrDefault(user.Id, user.RegistrationProvider ?? string.Empty),
                IsLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
                AccessFailedCount = user.AccessFailedCount,
                AppCount = appCount,
                RunnerCount = runnerCount,
                Apps = apps,
                Runners = runners,
                Roles = roles.ToList()
            };

            return userDetails;
        }
    }
}

