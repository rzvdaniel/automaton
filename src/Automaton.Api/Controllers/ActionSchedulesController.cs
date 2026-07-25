using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/action-schedules")]
public sealed class ActionSchedulesController : ControllerBase
{
    private readonly ActionScheduleService schedules;
    private readonly UserContextService users;
    public ActionSchedulesController(ActionScheduleService schedules, UserContextService users) { this.schedules = schedules; this.users = users; }
    private bool IsAdministrator => User.IsInRole("Administrators");

    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await schedules.ListAsync(users.GetUserId(), IsAdministrator, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await schedules.GetAsync(id, users.GetUserId(), IsAdministrator, ct));
    [HttpPost] public async Task<IActionResult> Create([FromBody] SaveActionScheduleRequest request, CancellationToken ct)
    {
        var value = await schedules.CreateAsync(request, users.GetUserId(), IsAdministrator, ct);
        return CreatedAtAction(nameof(Get), new { id = value.Id }, value);
    }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, [FromBody] SaveActionScheduleRequest request, CancellationToken ct) => Ok(await schedules.UpdateAsync(id, request, users.GetUserId(), IsAdministrator, ct));
    [HttpPost("{id:guid}/enable")] public async Task<IActionResult> Enable(Guid id, CancellationToken ct) => Ok(await schedules.SetEnabledAsync(id, true, users.GetUserId(), IsAdministrator, ct));
    [HttpPost("{id:guid}/disable")] public async Task<IActionResult> Disable(Guid id, CancellationToken ct) => Ok(await schedules.SetEnabledAsync(id, false, users.GetUserId(), IsAdministrator, ct));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await schedules.DeleteAsync(id, users.GetUserId(), IsAdministrator, ct); return NoContent(); }
}
