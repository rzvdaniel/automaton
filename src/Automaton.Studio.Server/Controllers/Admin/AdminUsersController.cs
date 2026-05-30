using AuthServer.Core.Queries;
using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Models;
using Common.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Controllers.Admin;

/// <summary>
/// Admin controller for platform-wide user management operations.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Administrators")]
public class AdminUsersController : BaseController
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDetails>>> GetUsers([FromQuery] FilterUserQuery filterUserQuery, CancellationToken cancellationToken)
    {
        return Ok(await Mediator.Send(filterUserQuery, cancellationToken));
    }

    [HttpGet("{userId:guid}", Name = "User")]
    public async Task<ActionResult<UserDetails>> GetUser([FromRoute, NotEmptyGuid] Guid userId)
    {
        return Ok(await Mediator.Send(new UserQuery { Id = userId }));
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
    public async Task<IActionResult> RemoveUserRole([FromRoute, NotEmptyGuid] Guid userId, [FromRoute, Required] string roleName)
    {
        await Mediator.Send(new RemoveUserRoleCommand(userId, roleName));
        return NoContent();
    }

    [HttpPut("{userId:guid}/roles/{roleName}")]
    public async Task<IActionResult> AddUserRole([FromRoute, NotEmptyGuid] Guid userId, [FromRoute, Required] string roleName)
    {
        await Mediator.Send(new AddUserRoleCommand(userId, roleName));
        return NoContent();
    }
}