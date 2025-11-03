using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class AppsController : BaseController
{
    private readonly AppsService appsService;

    public AppsController(AppsService appsService)
    {
        this.appsService = appsService;
    }

    [HttpGet]
    public IEnumerable<Models.AppModel> Get()
    {
        return appsService.List();
    }

    [HttpGet("{id}")]
    public ActionResult<Models.AppModel> Get(Guid id)
    {
        var app = appsService.Get(id);

        if (app is null)
        {
            return NotFound();
        }

        return Ok(app);
    }

    [HttpGet("slug/{slug}")]
    [AllowAnonymous]
    public ActionResult<Models.AppModel> GetBySlug(string slug)
    {
        var app = appsService.GetBySlug(slug);

        if (app is null)
        {
            return NotFound(new { message = "App not found or not published" });
        }

        return Ok(app);
    }

    [HttpPost]
    public async Task<IActionResult> Post(Models.NewApp app)
    {
        var appId = await appsService.Create(app);

        var newApp = appsService.Get(appId);

        return CreatedAtAction(nameof(Get), new { id = newApp.Id }, newApp);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, Models.AppModel app)
    {
        await appsService.Update(id, app);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await appsService.Remove(id);

        return NoContent();
    }

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAppRequest request)
    {
        await appsService.Publish(id, request.PublicUrl, request.RequiresAuthentication);

        return NoContent();
    }

    [HttpPost("{id}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        await appsService.Unpublish(id);

        return NoContent();
    }
}

public class PublishAppRequest
{
    public required string PublicUrl { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
}
