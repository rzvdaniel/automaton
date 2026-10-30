using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public class CustomStepsController : BaseController
{
    private readonly CustomStepsService customStepsService;

    public CustomStepsController(CustomStepsService customStepsService)
    {
        this.customStepsService = customStepsService;
    }

    [HttpGet]
    public IEnumerable<CustomStep> Get([FromQuery] Guid? projectId = null, [FromQuery] bool? excludeAppLinked = null)
    {
        return customStepsService.List(projectId, excludeAppLinked);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CustomStep>> Get(Guid id)
    {
        // Use GetForEditingAsync to decrypt sensitive variables for UI display
        var step = await customStepsService.GetForEditingAsync(id);

        if (step is null)
        {
            return NotFound();
        }

        return Ok(step);
    }

    [HttpPost]
    public async Task<IActionResult> Post(NewCustomStep customStep)
    {
        var customStepId = await customStepsService.Create(customStep);

        var newCustomStep = await customStepsService.GetForEditingAsync(customStepId);
        if (newCustomStep is null)
        {
            return NotFound();
        }

        return CreatedAtAction(nameof(Get), new { id = newCustomStep.Id }, newCustomStep);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, UpdateCustomStepRequest customStep)
    {
        await customStepsService.Update(id, customStep);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await customStepsService.Remove(id);

        return NoContent();
    }

    [HttpGet("exists/{name}")]
    public ActionResult<CustomStep> Exists(string name)
    {
        var exists = customStepsService.Exists(name);

        return exists ? Ok(exists) : NotFound();
    }

    [HttpGet("by-name/{name}")]
    public ActionResult<CustomStep> GetByName(string name)
    {
        var step = customStepsService.GetByName(name);

        if (step is null)
        {
            return NotFound();
        }

        return Ok(step);
    }

    [HttpPost("{id}/execute")]
    public async Task<ActionResult<IEnumerable<RunnerCustomStepResult>>> Execute(Guid id, [FromBody] ExecuteCustomStepRequest request, CancellationToken cancellationToken)
    {
        if (request?.RunnerIds == null || !request.RunnerIds.Any())
        {
            return BadRequest("At least one runner ID must be specified");
        }

        var results = await customStepsService.Execute(id, request.RunnerIds, cancellationToken);

        return Ok(results);
    }
}

/// <summary>
/// Request model for executing custom steps on runners
/// </summary>
public class ExecuteCustomStepRequest
{
    public IEnumerable<Guid> RunnerIds { get; set; } = Array.Empty<Guid>();
}
