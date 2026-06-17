using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Application.Queries.Handlers;

public class SignInUserQueryHandler : IRequestHandler<SignInUserQuery, JsonWebToken>
{
    private readonly UserManager<ApplicationUser> userManager;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly AuthTokenService authTokenService;
    private readonly UsersService usersService;

    public SignInUserQueryHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AuthTokenService authTokenService,
        UsersService usersService)
    {
        this.userManager = userManager;
        this.signInManager = signInManager;
        this.authTokenService = authTokenService;
        this.usersService = usersService;
    }

    public async Task<JsonWebToken> Handle(SignInUserQuery command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email) ??
                   await userManager.FindByNameAsync(command.Email);

        if (user == null)
        {
            throw new Exception("Invalid credentials.");
        }

        if (user.IsDeleted)
        {
            throw new Exception("Invalid credentials.");
        }

        if (!user.LockoutEnabled)
        {
            var lockoutResult = await userManager.SetLockoutEnabledAsync(user, true);
            if (!lockoutResult.Succeeded)
            {
                throw new Exception("Invalid credentials.");
            }
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            throw new Exception("Invalid credentials.");
        }

        await usersService.MarkSuccessfulLoginAsync(user);

        return await authTokenService.IssueForUserAsync(user, command.RefreshTokenExpirationDays, cancellationToken);
    }
}
