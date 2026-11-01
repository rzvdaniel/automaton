using Automaton.Api.Application.Commands;
using Automaton.Api.Application.Queries;
using Automaton.Api.Attributes;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// Admin controller for platform-wide user management operations.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Administrators")]
public class AdminUsersController : BaseController
{
    public sealed class HardDeleteUserRequest
    {
        public string? ConfirmationText { get; set; }
    }

    private readonly AdminAppsService adminAppsService;
    private readonly UsersService usersService;
    private readonly ConfigurationService configurationService;
    private readonly IEmailService emailService;
    private readonly AdminUserProvisioningService provisioning;

    public AdminUsersController(
        AdminAppsService adminAppsService,
        UsersService usersService,
        ConfigurationService configurationService,
        IEmailService emailService,
        AdminUserProvisioningService provisioning)
    {
        this.adminAppsService = adminAppsService;
        this.usersService = usersService;
        this.configurationService = configurationService;
        this.emailService = emailService;
        this.provisioning = provisioning;
    }

    /// <summary>Creates the account without a password and emails a one-time link to set it; resends for pending accounts.</summary>
    [HttpPost("invite")]
    public async Task<ActionResult<AdminProvisionedUser>> Invite([FromBody] AdminInviteUserRequest request, CancellationToken cancellationToken)
        => await Provision(() => provisioning.InviteAsync(request, User.Identity?.Name,
            User.IsInRole(UserRole.SystemAdministrator), cancellationToken));

    /// <summary>Creates the account with a password set by the administrator or generated and returned once; no email.</summary>
    [HttpPost]
    public async Task<ActionResult<AdminProvisionedUser>> Create([FromBody] AdminCreateUserRequest request, CancellationToken cancellationToken)
        => await Provision(() => provisioning.CreateAsync(request, User.IsInRole(UserRole.SystemAdministrator), cancellationToken));

    private async Task<ActionResult<AdminProvisionedUser>> Provision(Func<Task<AdminProvisionedUser>> action)
    {
        try { return Ok(await action()); }
        catch (AdminUserConflictException exception) { return Conflict(new { message = exception.Message }); }
        catch (UnauthorizedAccessException exception) { return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        // Identity rejected the password (policy) or a duplicate slipped past the check.
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpGet]
    public async Task<ActionResult<AdminUserListResponse>> GetUsers([FromQuery] FilterUserQuery filterUserQuery, CancellationToken cancellationToken)
    {
        return Ok(await Mediator.Send(filterUserQuery, cancellationToken));
    }

    [HttpGet("{userId:guid}", Name = "User")]
    public async Task<ActionResult<UserDetails>> GetUser([FromRoute, NotEmptyGuid] Guid userId)
    {
        return Ok(await Mediator.Send(new UserQuery { Id = userId }));
    }

    [HttpGet("{userId:guid}/apps")]
    public ActionResult<IEnumerable<AppModel>> GetUserApps([FromRoute, NotEmptyGuid] Guid userId, [FromQuery] Guid? projectId = null)
    {
        return Ok(adminAppsService.ListOwnedByUserForAdmin(userId, projectId));
    }

    [HttpPut("{userId:guid}")]
    public Task UpdateUser([FromRoute, NotEmptyGuid] Guid userId, [FromBody] UpdateUserInfoCommand profileUpdateCommand)
    {
        profileUpdateCommand.Id = userId;
        return Mediator.Send(profileUpdateCommand);
    }

    [HttpGet("{userId:guid}/roles", Name = "Roles")]
    public async Task<ActionResult<IEnumerable<string>>> GetUserRoles([FromRoute, NotEmptyGuid] Guid userId)
    {
        var data = await Mediator.Send(new UserRolesQuery { UserId = userId });
        return Ok(data);
    }

    [HttpDelete("{userId:guid}/roles/{roleName}")]
    public async Task<IActionResult> RemoveUserRole([FromRoute, NotEmptyGuid] Guid userId, [FromRoute, Required] string roleName, CancellationToken cancellationToken)
    {
        try
        {
            await usersService.EnsureCanRemoveRoleAsync(
                GetUserId(),
                userId,
                roleName,
                User.IsInRole(UserRole.SystemAdministrator),
                cancellationToken);

            await Mediator.Send(new RemoveUserRoleCommand(userId, roleName), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{userId:guid}/roles/{roleName}")]
    public async Task<IActionResult> AddUserRole([FromRoute, NotEmptyGuid] Guid userId, [FromRoute, Required] string roleName, CancellationToken cancellationToken)
    {
        try
        {
            await usersService.EnsureCanAddRoleAsync(
                GetUserId(),
                userId,
                roleName,
                User.IsInRole(UserRole.SystemAdministrator),
                cancellationToken);

            await Mediator.Send(new AddUserRoleCommand(userId, roleName), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{userId:guid}/disable")]
    public async Task<IActionResult> DisableUser([FromRoute, NotEmptyGuid] Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await usersService.EnsureCanManageUserLockStateAsync(GetUserId(), userId, cancellationToken);

            await usersService.SetDisabledAsync(userId, disabled: true, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{userId:guid}/enable")]
    public async Task<IActionResult> EnableUser([FromRoute, NotEmptyGuid] Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await usersService.EnsureCanManageUserLockStateAsync(GetUserId(), userId, cancellationToken);

            await usersService.SetDisabledAsync(userId, disabled: false, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{userId:guid}/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromRoute, NotEmptyGuid] Guid userId, CancellationToken cancellationToken)
    {
        var user = await usersService.GetUserById(userId);
        if (user.EmailConfirmed)
        {
            return NoContent();
        }

        await SendEmailConfirmationAsync(user, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/hard")]
    public async Task<IActionResult> HardDeleteUser(
        [FromRoute, NotEmptyGuid] Guid userId,
        [FromBody] HardDeleteUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request?.ConfirmationText, "DELETE", StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Confirmation text must be exactly DELETE." });
        }

        try
        {
            await usersService.EnsureCanHardDeleteUserAsync(
                GetUserId(),
                userId,
                User.IsInRole(UserRole.SystemAdministrator),
                cancellationToken);

            await usersService.HardDeleteAsync(userId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var email = user.Email ?? throw new InvalidOperationException("A user email is required for email confirmation.");
        var token = await usersService.GenerateEmailConfirmationTokenAsync(user.Id);
        var confirmationUrl = BuildEmailConfirmationUrl(user.Id, token);

        var userDetails = new EmailData
        {
            Template = "AutomatonConfirmEmail",
            From = new EmailAccount { Email = ResolveUserFacingFromEmail() },
            To = [new EmailAccount { Email = email, Name = user.Name }],
            Variables =
            {
                { "Name", user.Name },
                { "ConfirmationUrl", confirmationUrl },
                { "LoginUrl", $"{configurationService.WebsiteUrl}/login" },
                { "SupportEmail", configurationService.EmailConfig.SupportEmail }
            }
        };

        await emailService.Send(userDetails, cancellationToken);
    }

    private string BuildEmailConfirmationUrl(Guid userId, string token)
    {
        var apiBaseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            $"{apiBaseUrl}/api/Account/ConfirmEmail",
            new Dictionary<string, string?>
            {
                ["userId"] = userId.ToString(),
                ["token"] = token
            });
    }

    private string ResolveUserFacingFromEmail()
    {
        return string.IsNullOrWhiteSpace(configurationService.EmailConfig.FromEmail)
            ? configurationService.EmailConfig.EmailAccount
            : configurationService.EmailConfig.FromEmail;
    }
}
