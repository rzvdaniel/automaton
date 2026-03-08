using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Common.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[Route("api/[controller]/[action]")]
public class AccountController : BaseController
{
    private readonly ConfigurationService configurationService;
    private readonly ApplicationDbContext dataContext;
    private readonly UsersService userManagerService;
    private readonly IEmailService emailService;

    public AccountController(ConfigurationService configurationService,
        ApplicationDbContext dataContext, 
        UsersService userManagerService,
        IEmailService emailService)
    {
        this.configurationService = configurationService;
        this.dataContext = dataContext;
        this.userManagerService = userManagerService;
        this.emailService = emailService;
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

        // Validate or auto-generate public username
        string publicUsername;
        
        if (!string.IsNullOrWhiteSpace(registerUserCommand.PublicUsername))
        {
            // Validate user-provided public username
            if (!userManagerService.IsValidPublicUsername(registerUserCommand.PublicUsername))
            {
                ModelState.AddModelError("PublicUsername", "Public username must be 3-32 characters, using only letters, numbers, hyphens, and underscores.");
                return BadRequest(ModelState);
            }

            // Check if available
            var isAvailable = await userManagerService.IsPublicUsernameAvailableAsync(registerUserCommand.PublicUsername);
            if (!isAvailable)
            {
                ModelState.AddModelError("PublicUsername", "This public username is already taken. Please choose another.");
                return BadRequest(ModelState);
            }

            publicUsername = registerUserCommand.PublicUsername;
        }
        else
        {
            // Auto-generate public username from display name
            publicUsername = await userManagerService.GenerateUniquePublicUsernameAsync(registerUserCommand.Name!);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Name = registerUserCommand.Name,
            Email = registerUserCommand.Email,
            UserName = registerUserCommand.Email,
            PublicUsername = publicUsername,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        await userManagerService.CreateUser(user, registerUserCommand.Password);
        await dataContext.SaveChangesAsync();

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
            new { name = registerUserCommand.Name, publicUsername = publicUsername });
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

    /// <summary>
    /// Get current user's public username
    /// GET /api/Account/GetPublicUsername
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<string>> GetPublicUsername()
    {
        var userId = GetUserId();
        var user = await userManagerService.GetUserById(userId);
        
        return Ok(user.PublicUsername);
    }

    /// <summary>
    /// Check if a public username is available
    /// GET /api/Account/IsPublicUsernameAvailable/{publicUsername}
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> IsPublicUsernameAvailable(string publicUsername)
    {
        var isAvailable = await userManagerService.IsPublicUsernameAvailableAsync(publicUsername);
        return Ok(isAvailable);
    }

    /// <summary>
    /// Update current user's public username
    /// PUT /api/Account/UpdatePublicUsername
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdatePublicUsername([FromBody] UpdatePublicUsernameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.NewPublicUsername))
        {
            ModelState.AddModelError("NewPublicUsername", "Public username is required");
            return BadRequest(ModelState);
        }

        // Validate format
        if (!userManagerService.IsValidPublicUsername(request.NewPublicUsername))
        {
            ModelState.AddModelError("NewPublicUsername", "Public username must be 3-32 characters, using only letters, numbers, hyphens, and underscores.");
            return BadRequest(ModelState);
        }

    var userId = GetUserId();
    var currentUser = await userManagerService.GetUserById(userId);
    
    // Only check availability if username is actually changing
    if (!string.Equals(currentUser?.PublicUsername, request.NewPublicUsername, StringComparison.OrdinalIgnoreCase))
    {
        var isAvailable = await userManagerService.IsPublicUsernameAvailableAsync(request.NewPublicUsername);
        if (!isAvailable)
        {
            ModelState.AddModelError("NewPublicUsername", "This public username is already taken. Please choose another.");
            return BadRequest(ModelState);
        }
    }

        await userManagerService.UpdatePublicUsernameAsync(userId, request.NewPublicUsername);
        await dataContext.SaveChangesAsync();

        return Ok(new { publicUsername = request.NewPublicUsername });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ResetUserPassword(ResetUserPasswordCommand resetUserPassword, CancellationToken cancellationToken)
    {
        await Mediator.Send(resetUserPassword, cancellationToken);

        return NoContent();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetPlanFeatures([FromServices] ISubscriptionGatewayService subscriptionGateway)
    {
        var userId = GetUserId();
        var (canCreateProjects, canCreateOrganizations) = await subscriptionGateway.GetPlanFeaturesAsync(userId);
        return Ok(new { canCreateProjects, canCreateOrganizations });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSubscriptionPlans([FromServices] Data.ApplicationDbContext db)
    {
        var plans = await db.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.MonthlyCostLimitUsd,
                p.CanCreateProjects,
                p.CanCreateOrganizations,
                p.IsDefault,
                p.SortOrder
            })
            .ToListAsync();

        return Ok(plans);
    }
}