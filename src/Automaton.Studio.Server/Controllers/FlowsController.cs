using Automaton.Core.Models;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class FlowsController : BaseController
{
    private readonly FlowsService flowsService;

    public FlowsController(FlowsService flowsService)
    {
        this.flowsService = flowsService;
    }

    [HttpGet]
    public IEnumerable<FlowInfo> Get([FromQuery] Guid? projectId = null)
    {
        return flowsService.List(projectId);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult<Flow> Get(Guid id)
    {
        var flow = flowsService.Get(id);

        if (flow is null)
        {
            return NotFound();
        }

        return Ok(flow);
    }

    [HttpPost]
    public async Task<IActionResult> Post(Flow flow)
    {
        var flowId = await flowsService.Create(flow);

        var newFlow = flowsService.Get(flowId);

        return CreatedAtAction(nameof(Get), new { id = newFlow.Id }, newFlow);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, Flow flow)
    {
        await flowsService.Update(id, flow);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await flowsService.Remove(id);

        return NoContent();
    }

    [HttpPost("run")]
    public async Task<ActionResult> Post(ExecuteFlowDetails command, CancellationToken cancellationToken)
    {
        var result = await flowsService.Execute(command.FlowId, command.RunnerIds, cancellationToken);

        return Ok(result);
    }

    [HttpGet("exists/{name}")]
    public ActionResult<Flow> Exists(string name)
    {
        var exists = flowsService.Exists(name);

        return exists ? Ok(exists) : NotFound();
    }

    [HttpGet("settings/{id}")]
    public ActionResult<FlowSettings> Dashboard(Guid id)
    {
        var flow = flowsService.GetSettings(id);

        if (flow is null)
        {
            return NotFound();
        }

        return Ok(flow);
    }
}