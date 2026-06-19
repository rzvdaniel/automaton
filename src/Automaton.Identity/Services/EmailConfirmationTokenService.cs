using Automaton.Api.Entities;
using Automaton.Identity.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Identity.Services;

public sealed class EmailConfirmationTokenService(
    UserManager<ApplicationUser> userManager,
    IUserLookupService userLookupService) : IEmailConfirmationTokenService
{
    public async Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userLookupService.GetByIdAsync(userId, cancellationToken);
        return await userManager.GenerateEmailConfirmationTokenAsync(user);
    }
}
