using Automaton.Api.Application.Queries;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[Route("api/[controller]/[action]")]
public class UserController : BaseController
{
    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<UserDetails>> GetUserProfile()
    {
        return Ok(await Mediator.Send(new UserQuery { Id = GetUserId() }));
    }
}