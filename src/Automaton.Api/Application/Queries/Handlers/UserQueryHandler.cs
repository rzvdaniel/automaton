using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Application.Queries;
using Automaton.Api.Services;

namespace Automaton.Api.Application.Queries.Handlers
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
            var roles = await _userManager.GetRolesAsync(user);
            var appCount = await GetAppCountAsync(user.Id, cancellationToken);
            var publishedAppCount = await GetPublishedAppCountAsync(user.Id, cancellationToken);
            var runnerCount = await GetRunnerCountAsync(user.Id, cancellationToken);
            var apps = await GetAppsAsync(user.Id, cancellationToken);
            var lastAppName = await GetLastAppNameAsync(user.Id, cancellationToken);
            var runners = await GetRunnersAsync(user.Id, cancellationToken);

            return MapUser(
                user,
                registrationProviders,
                roles,
                appCount,
                publishedAppCount,
                runnerCount,
                apps,
                lastAppName,
                runners);
        }

        private Task<int> GetAppCountAsync(Guid userId, CancellationToken cancellationToken)
            => _db.WebAppUsers.CountAsync(item => item.UserId == userId, cancellationToken);

        private Task<int> GetPublishedAppCountAsync(Guid userId, CancellationToken cancellationToken)
            => _db.WebAppUsers
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .Join(
                    _db.WebApps.AsNoTracking(),
                    webAppUser => webAppUser.WebAppId,
                    webApp => webApp.Id,
                    (_, webApp) => webApp)
                .CountAsync(webApp => webApp.IsPublished, cancellationToken);

        private Task<int> GetRunnerCountAsync(Guid userId, CancellationToken cancellationToken)
            => _db.RunnerUsers.CountAsync(item => item.UserId == userId, cancellationToken);

        private Task<List<UserAppLink>> GetAppsAsync(Guid userId, CancellationToken cancellationToken)
            => _db.WebAppUsers
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .Join(
                    _db.WebApps.AsNoTracking(),
                    webAppUser => webAppUser.WebAppId,
                    webApp => webApp.Id,
                    (_, webApp) => new UserAppLink
                    {
                        Id = webApp.Id,
                        Name = webApp.Name,
                        IsPublished = webApp.IsPublished,
                    })
                .OrderBy(app => app.Name)
                .ToListAsync(cancellationToken);

        private Task<string?> GetLastAppNameAsync(Guid userId, CancellationToken cancellationToken)
            => _db.WebAppUsers
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .Join(
                    _db.WebApps.AsNoTracking(),
                    webAppUser => webAppUser.WebAppId,
                    webApp => webApp.Id,
                    (_, webApp) => new { webApp.Name, webApp.Updated, webApp.Created })
                .OrderByDescending(app => app.Updated)
                .ThenByDescending(app => app.Created)
                .Select(app => app.Name)
                .FirstOrDefaultAsync(cancellationToken);

        private Task<List<UserRunnerLink>> GetRunnersAsync(Guid userId, CancellationToken cancellationToken)
            => _db.RunnerUsers
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .Join(
                    _db.Runners.AsNoTracking(),
                    runnerUser => runnerUser.RunnerId,
                    runner => runner.Id,
                    (_, runner) => new UserRunnerLink
                    {
                        Id = runner.Id,
                        Name = runner.Name,
                        Status = RunnerPresenceEvaluator.DeriveStatus(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow).ToString(),
                        LastHeartbeatAtUtc = runner.LastHeartbeatAtUtc,
                        LastConnectedAtUtc = runner.LastConnectedAtUtc,
                        Version = runner.Version,
                    })
                .OrderBy(runner => runner.Name)
                .ToListAsync(cancellationToken);

        private static UserDetails MapUser(
            ApplicationUser user,
            IReadOnlyDictionary<Guid, string> registrationProviders,
            IList<string> roles,
            int appCount,
            int publishedAppCount,
            int runnerCount,
            List<UserAppLink> apps,
            string? lastAppName,
            List<UserRunnerLink> runners)
        {
            var onlineRunnerCount = runners.Count(runner => string.Equals(runner.Status, "Online", StringComparison.OrdinalIgnoreCase));
            var lastRunnerHeartbeatAtUtc = runners
                .Where(runner => runner.LastHeartbeatAtUtc.HasValue)
                .OrderByDescending(runner => runner.LastHeartbeatAtUtc)
                .Select(runner => runner.LastHeartbeatAtUtc)
                .FirstOrDefault();

            return new UserDetails
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
                PublishedAppCount = publishedAppCount,
                PrivateAppCount = appCount - publishedAppCount,
                LastAppName = lastAppName,
                RunnerCount = runnerCount,
                OnlineRunnerCount = onlineRunnerCount,
                LastRunnerHeartbeatAtUtc = lastRunnerHeartbeatAtUtc,
                Apps = apps,
                Runners = runners,
                Roles = roles.ToList()
            };
        }
    }
}
