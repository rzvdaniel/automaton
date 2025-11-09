using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services;
using Common.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

[Route("api/[controller]/[action]")]
public class AccountController : BaseController
{
    private readonly ConfigurationService configurationService;
    private readonly ApplicationDbContext dataContext;
    private readonly UsersService userManagerService;
    private readonly EmailService emailService;
    private readonly ProjectService projectService;

    public AccountController(ConfigurationService configurationService,
        ApplicationDbContext dataContext, 
        UsersService userManagerService,
        EmailService emailService,
        ProjectService projectService)
    {
        this.configurationService = configurationService;
        this.dataContext = dataContext;
        this.userManagerService = userManagerService;
        this.emailService = emailService;
        this.projectService = projectService;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterUser(RegisterUserCommand registerUserCommand)
    {
        if (configurationService.NoUserSignUp)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = Guid.NewGuid();
        
        await userManagerService.CreateUser(new ApplicationUser
        {
            Id = userId,
            Name = registerUserCommand.Name,
            Email = registerUserCommand.Email,
            UserName = registerUserCommand.Email,
            SecurityStamp = Guid.NewGuid().ToString()
        }, registerUserCommand.Password);

        await dataContext.SaveChangesAsync();

        // Create default project for new user
        try
        {
            await projectService.CreateDefaultProjectForUserAsync(userId, registerUserCommand.Name);
        }
        catch (Exception ex)
        {
            // Log error but don't fail registration if project creation fails
            // User can create project manually later
            var logger = HttpContext.RequestServices.GetService<ILogger<AccountController>>();
            logger?.LogError(ex, "Failed to create default project for user {UserId}", userId);
        }

        var userDetails = new EmailData { Template = "AutomatonRegisterUser", 
            From = new EmailAccount { Email = configurationService.EmailConfig.EmailSupport }, 
            To = [new EmailAccount { Email = registerUserCommand.Email, Name = registerUserCommand.Name }],
            Variables = 
            {
                { "FullName", registerUserCommand.Name },
                { "WebsiteUrl", configurationService.WebsiteUrl },
                { "SupportEmail", configurationService.EmailConfig.EmailSupport }
            }
        };
       
        await emailService.Send(userDetails, CancellationToken.None);

        return CreatedAtRoute("User",
            new { controller = "User", userId = registerUserCommand.Email },
            registerUserCommand.Name);
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<JsonWebToken>> LoginUser(SignInUserCommand signInUserCommand, CancellationToken cancellationToken)
    {
        var signInUserQuery = new SignInUserQuery(configurationService.RefreshTokenLifetime, signInUserCommand.Email, signInUserCommand.Password);

        return Ok(await Mediator.Send(signInUserQuery, cancellationToken));
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<JsonWebToken>> LoginRunnerUser(SignInUserCommand signInUserCommand, CancellationToken cancellationToken)
    {
        var signInUserQuery = new SignInUserQuery(configurationService.RunnerRefreshTokenLifetime, signInUserCommand.Email, signInUserCommand.Password);

        return Ok(await Mediator.Send(signInUserQuery, cancellationToken));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateUserPassword(UpdateUserPasswordCommand passwordUpdateCommand, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        await userManagerService.UpdatePassword
        (
            userId, 
            passwordUpdateCommand.OldPassword, 
            passwordUpdateCommand.NewPassword
        );

        await dataContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateUserProfile(UpdateUserInfoCommand profileUpdateCommand, CancellationToken cancellationToken)
    {
        profileUpdateCommand.Id = GetUserId();

        await Mediator.Send(profileUpdateCommand, cancellationToken);

        return NoContent();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotUserPassword(ForgotUserPasswordCommand forgotUserPassword, CancellationToken cancellationToken)
    {
        await Mediator.Send(forgotUserPassword, cancellationToken);

        return NoContent();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ResetUserPassword(ResetUserPasswordCommand resetUserPassword, CancellationToken cancellationToken)
    {
        await Mediator.Send(resetUserPassword, cancellationToken);

        return NoContent();
    }
}