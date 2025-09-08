using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class InstallController : BaseController
{
    private readonly InstallService installService;

    public InstallController(InstallService installService)
    {
        this.installService = installService;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInstallationStatus()
    {
        var isComplete = await installService.InstallationComplete();
        return Ok(new { InstallationComplete = isComplete });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Post(InstallCommand installCommand, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (await installService.InstallationComplete())
        {
            return BadRequest("Installation has already been completed.");
        }

        await installService.Install(installCommand, cancellationToken);

        return Ok();
    }
}