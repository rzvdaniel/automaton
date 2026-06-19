using Automaton.Api.Entities;
using Automaton.Identity.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Identity.Services;

public sealed class UserLookupService(UserManager<ApplicationUser> userManager) : IUserLookupService
{
    public async Task<ApplicationUser> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await userManager.FindByIdAsync(userId.ToString());
        if (result == null || result.IsDeleted)
        {
            throw new Exception($"User with userId {userId} not found!");
        }

        return result;
    }
}
