using Automaton.Api.Application.Queries;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Application.Queries.Handlers
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
            var res = await _userManagerService.GetUsers(CreateUserFilter(request));
            var userIds = res.Items.Select(user => user.Id).ToList();
            var appCounts = await LoadAppCountsAsync(userIds, cancellationToken);
            var runnerCounts = await LoadRunnerCountsAsync(userIds, cancellationToken);
            var registrationProviders = await _userManagerService.ResolveRegistrationProvidersAsync(res.Items, cancellationToken);
            return CreateResponse(res, registrationProviders, appCounts, runnerCounts);
        }

        private static UserFilter CreateUserFilter(FilterUserQuery request)
            => new()
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
                PageIndex = request.PageIndex ?? 0,
                PageSize = request.PageSize ?? 100
            };

        private Task<Dictionary<Guid, int>> LoadAppCountsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken)
            => _db.WebAppUsers
                .AsNoTracking()
                .Where(webAppUser => userIds.Contains(webAppUser.UserId))
                .GroupBy(webAppUser => webAppUser.UserId)
                .Select(group => new { UserId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);

        private Task<Dictionary<Guid, int>> LoadRunnerCountsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken)
            => _db.RunnerUsers
                .AsNoTracking()
                .Where(runnerUser => userIds.Contains(runnerUser.UserId))
                .GroupBy(runnerUser => runnerUser.UserId)
                .Select(group => new { UserId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);

        private static AdminUserListResponse CreateResponse(
            ApplicationUserPageResult result,
            IReadOnlyDictionary<Guid, string> registrationProviders,
            IReadOnlyDictionary<Guid, int> appCounts,
            IReadOnlyDictionary<Guid, int> runnerCounts)
            => new()
            {
                Items = result.Items.Select(user => MapUser(user, registrationProviders, appCounts, runnerCounts)).ToList(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
                Summary = result.Summary
            };

        private static UserDetails MapUser(
            ApplicationUser user,
            IReadOnlyDictionary<Guid, string> registrationProviders,
            IReadOnlyDictionary<Guid, int> appCounts,
            IReadOnlyDictionary<Guid, int> runnerCounts)
            => new()
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
            };
    }
}
