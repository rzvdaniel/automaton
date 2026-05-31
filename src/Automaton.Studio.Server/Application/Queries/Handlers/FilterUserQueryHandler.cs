using AuthServer.Core.Queries;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Application.Queries.Handlers
{
    public class FilterUserQueryHandler : IRequestHandler<FilterUserQuery, AdminUserListResponse>
    {
        private readonly UsersService _userManagerService;
        private readonly ApplicationDbContext _db;

        public FilterUserQueryHandler(UsersService userManagerService, ApplicationDbContext db)
        {
            _userManagerService = userManagerService;
            _db = db;
        }

        public async Task<AdminUserListResponse> Handle(FilterUserQuery request,
            CancellationToken cancellationToken)
        {
            var res = await _userManagerService.GetUsers(new UserFilter
            {
                Search = request.Search,
                Name = request.Name,
                Email = request.Email,
                RegistrationProvider = request.RegistrationProvider,
                EmailConfirmed = request.EmailConfirmed,
                IsLocked = request.IsLocked,
                RegisteredFromUtc = request.RegisteredFromUtc,
                RegisteredToUtc = request.RegisteredToUtc,
                SortField = request.SortField,
                SortOrder = request.SortOrder,
                PageIndex = request.PageIndex,
                PageSize = request.PageSize
            });

            var userIds = res.Items.Select(user => user.Id).ToList();

            var appCounts = await _db.WebAppUsers
                .AsNoTracking()
                .Where(webAppUser => userIds.Contains(webAppUser.UserId))
                .GroupBy(webAppUser => webAppUser.UserId)
                .Select(group => new { UserId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);

            var runnerCounts = await _db.RunnerUsers
                .AsNoTracking()
                .Where(runnerUser => userIds.Contains(runnerUser.UserId))
                .GroupBy(runnerUser => runnerUser.UserId)
                .Select(group => new { UserId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);

            var registrationProviders = await _userManagerService.ResolveRegistrationProvidersAsync(res.Items, cancellationToken);

            var items = res.Items.Select(user => new UserDetails
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
                AppCount = appCounts.GetValueOrDefault(user.Id),
                RunnerCount = runnerCounts.GetValueOrDefault(user.Id),
                Roles = user.UserRoles
                    .Where(userRole => userRole.Role != null)
                    .Select(userRole => userRole.Role.Name)
                    .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                    .Cast<string>()
                    .ToList()
            }).ToList();

            return new AdminUserListResponse
            {
                Items = items,
                TotalCount = res.TotalCount,
                Page = res.Page,
                PageSize = res.PageSize,
                Summary = res.Summary,
            };
        }
    }
}
