using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Services;

public class ConfirmedEmailGuardService
{
    public const string SensitiveActionMessage = "Please confirm your email before performing this action.";

    private readonly UsersService usersService;

    public ConfirmedEmailGuardService(UsersService usersService)
    {
        this.usersService = usersService;
    }

    public async Task<bool> IsConfirmedAsync(Guid userId)
    {
        var user = await usersService.GetUserById(userId);
        return user.EmailConfirmed;
    }

    public async Task<ApplicationUser> GetConfirmedUserOrThrowAsync(Guid userId)
    {
        var user = await usersService.GetUserById(userId);
        if (!user.EmailConfirmed)
        {
            throw new InvalidOperationException(SensitiveActionMessage);
        }

        return user;
    }
}
