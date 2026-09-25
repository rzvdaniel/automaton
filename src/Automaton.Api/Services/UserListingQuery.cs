using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

internal static class UserListingQuery
{
    public static IQueryable<ApplicationUser> Create(
        IQueryable<ApplicationUser> users,
        UserFilter filter,
        DateTimeOffset now)
    {
        var query = users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Where(user => !user.IsDeleted);

        return ApplyRegistrationFilters(
            ApplyStatusFilters(
                ApplyProfileFilters(
                    ApplySearchFilter(query, filter.Search?.Trim()),
                    filter),
                filter,
                now),
            filter);
    }

    public static async Task<UserListSummary> GetSummaryAsync(
        IQueryable<ApplicationUser> query,
        DateTimeOffset now,
        DateTime todayUtc,
        DateTime weekStartUtc,
        DateTime monthStartUtc)
    {
        return new UserListSummary
        {
            TotalCount = await query.CountAsync(),
            VerifiedCount = await query.CountAsync(user => user.EmailConfirmed),
            ActiveCount = await query.CountAsync(user => !user.LockoutEnd.HasValue || user.LockoutEnd.Value <= now),
            JoinedTodayCount = await query.CountAsync(user => user.RegisteredAtUtc >= todayUtc),
            JoinedThisWeekCount = await query.CountAsync(user => user.RegisteredAtUtc >= weekStartUtc),
            JoinedThisMonthCount = await query.CountAsync(user => user.RegisteredAtUtc >= monthStartUtc),
        };
    }

    public static Task<List<ApplicationUser>> GetPageAsync(
        IQueryable<ApplicationUser> query,
        UserFilter filter,
        int pageIndex,
        int pageSize)
    {
        return ApplySorting(query, filter.SortField?.Trim(), filter.SortOrder == -1)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    private static IQueryable<ApplicationUser> ApplySearchFilter(
        IQueryable<ApplicationUser> query,
        string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        return query.Where(user =>
            (user.Name != null && user.Name.Contains(searchTerm)) ||
            (user.Email != null && user.Email.Contains(searchTerm)) ||
            (user.PublicUsername != null && user.PublicUsername.Contains(searchTerm)));
    }

    private static IQueryable<ApplicationUser> ApplyProfileFilters(
        IQueryable<ApplicationUser> query,
        UserFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(user => user.Name != null && user.Name.StartsWith(filter.Name));
        }

        if (!string.IsNullOrWhiteSpace(filter.Email))
        {
            query = query.Where(user => user.Email == filter.Email);
        }

        if (!string.IsNullOrWhiteSpace(filter.RegistrationProvider))
        {
            query = query.Where(user => user.RegistrationProvider == filter.RegistrationProvider);
        }

        return query;
    }

    private static IQueryable<ApplicationUser> ApplyStatusFilters(
        IQueryable<ApplicationUser> query,
        UserFilter filter,
        DateTimeOffset now)
    {
        if (filter.EmailConfirmed.HasValue)
        {
            query = query.Where(user => user.EmailConfirmed == filter.EmailConfirmed.Value);
        }

        if (filter.IsLocked.HasValue)
        {
            query = query.Where(user =>
                (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now) == filter.IsLocked.Value);
        }

        return query;
    }

    private static IQueryable<ApplicationUser> ApplyRegistrationFilters(
        IQueryable<ApplicationUser> query,
        UserFilter filter)
    {
        if (filter.RegisteredFromUtc.HasValue)
        {
            query = query.Where(user => user.RegisteredAtUtc >= filter.RegisteredFromUtc.Value);
        }

        if (filter.RegisteredToUtc.HasValue)
        {
            query = query.Where(user => user.RegisteredAtUtc <= filter.RegisteredToUtc.Value);
        }

        return query;
    }

    private static IOrderedQueryable<ApplicationUser> ApplySorting(
        IQueryable<ApplicationUser> query,
        string? sortField,
        bool sortDescending)
    {
        return sortField switch
        {
            "name" => SortByName(query, sortDescending),
            "lastLoginAtUtc" => SortByLastLogin(query, sortDescending),
            "registeredAtUtc" => SortByRegistration(query, sortDescending),
            _ => SortByLastLogin(query, descending: true),
        };
    }

    private static IOrderedQueryable<ApplicationUser> SortByName(
        IQueryable<ApplicationUser> query,
        bool descending)
    {
        return descending ? query.OrderByDescending(user => user.Name) : query.OrderBy(user => user.Name);
    }

    private static IOrderedQueryable<ApplicationUser> SortByLastLogin(
        IQueryable<ApplicationUser> query,
        bool descending)
    {
        return descending
            ? query.OrderByDescending(user => user.LastLoginAtUtc != null)
                .ThenByDescending(user => user.LastLoginAtUtc)
                .ThenByDescending(user => user.RegisteredAtUtc)
            : query.OrderBy(user => user.LastLoginAtUtc == null ? 1 : 0)
                .ThenBy(user => user.LastLoginAtUtc)
                .ThenBy(user => user.RegisteredAtUtc);
    }

    private static IOrderedQueryable<ApplicationUser> SortByRegistration(
        IQueryable<ApplicationUser> query,
        bool descending)
    {
        return descending
            ? query.OrderByDescending(user => user.RegisteredAtUtc)
            : query.OrderBy(user => user.RegisteredAtUtc);
    }
}
