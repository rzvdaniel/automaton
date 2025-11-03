using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class AppsController : BaseController
{
    private readonly AppsService appsService;
    private readonly UserImpersonationServiceFactory<AppsService> appsServiceFactory;
    private readonly ILogger<AppsController> logger;

    public AppsController(
        AppsService appsService,
        UserImpersonationServiceFactory<AppsService> appsServiceFactory,
        ILogger<AppsController> logger)
    {
        this.appsService = appsService;
        this.appsServiceFactory = appsServiceFactory;
        this.logger = logger;
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

    /// <summary>
    /// Get published app by public URL slug.
    /// Used by WebAppViewer to load apps accessed via /web/{slug} URLs.
    /// Validates authentication requirements and loads app as the owner.
    /// Note: We check authentication ourselves based on app settings
    /// </summary>
    [HttpGet("slug/{slug}")]
    [AllowAnonymous]
    public async Task<ActionResult<Models.AppModel>> GetBySlug(string slug)
    {
        try
        {
            logger.LogInformation("[AppsController] Loading app by slug: {Slug}", slug);

            // 1. Get published app metadata (without requiring ownership)
            var appInfo = appsService.GetPublishedAppInfo(slug);

            if (appInfo == null)
            {
                logger.LogWarning("[AppsController] App not found or not published: {Slug}", slug);
                return NotFound(new { message = "App not found or not published" });
            }

            // 2. Check authentication requirements
            var isUserAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var viewerName = User.Identity?.Name ?? "Anonymous";

            if (appInfo.RequiresAuthentication && !isUserAuthenticated)
            {
                logger.LogWarning("[AppsController] Unauthenticated access to protected app: {AppName} (slug: {Slug})", appInfo.Name, slug);
                return Unauthorized(new { message = "This app requires authentication" });
            }

            // 3. Validate app has owner
            if (appInfo.OwnerId == null)
            {
                logger.LogError("[AppsController] App has no owner: {AppName} (slug: {Slug})", appInfo.Name, slug);
                return StatusCode(500, new { message = "App configuration error: no owner" });
            }

            // 4. Log access for traceability and security auditing
            logger.LogInformation(
                "[AppsController] App access: Slug={Slug}, AppId={AppId}, AppName={AppName}, " +
                "Owner={OwnerId}, Viewer={Viewer}, Authenticated={IsAuth}, RequiresAuth={RequiresAuth}",
                slug, appInfo.Id, appInfo.Name, appInfo.OwnerId, viewerName,
                isUserAuthenticated, appInfo.RequiresAuthentication);

            // 5. Load app as owner (with impersonation)
            var ownerAppsService = appsServiceFactory.CreateForUser(appInfo.OwnerId.Value);
            var app = ownerAppsService.Get(appInfo.Id);

            if (app == null)
            {
                logger.LogError("[AppsController] Failed to load app as owner: {AppId}", appInfo.Id);
                return NotFound(new { message = "App not found" });
            }

            logger.LogInformation("[AppsController] Successfully loaded app: {AppName} (ID: {AppId})", app.Name, app.Id);

            return Ok(app);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error loading app by slug: {Slug}", slug);
            return StatusCode(500, new { message = "An error occurred while loading the app" });
        }
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
