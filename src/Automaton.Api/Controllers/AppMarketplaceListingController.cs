using Automaton.Api.Authentication;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// How an app is presented in the public marketplace. Images use the existing
/// cover and screenshot endpoints on <see cref="AppsController"/>.
/// </summary>
[ApiController]
[Route("api/apps/{id:guid}/listing")]
public sealed class AppMarketplaceListingController(AppMarketplaceListingService listingService) : ControllerBase
{
    // Only the fields sent change; an empty category clears it.
    [HttpPut]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Update(Guid id, UpdateAppListingRequest request, CancellationToken cancellationToken)
    {
        if (!AppChangeKeyScope.IsAllowed(User))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = AppChangeKeyScope.DeniedMessage });
        var result = await listingService.UpdateAsync(id, request, cancellationToken);
        return result.Status switch
        {
            AppListingUpdateStatus.Updated => NoContent(),
            AppListingUpdateStatus.NotFound => NotFound(new { message = "App not found" }),
            AppListingUpdateStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have permission to change this app." }),
            _ => BadRequest(new { message = result.Error })
        };
    }
}
