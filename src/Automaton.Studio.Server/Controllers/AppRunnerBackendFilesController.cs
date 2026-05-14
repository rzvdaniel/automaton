using Automaton.Core.Models;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/runner-backend/files")]
[Authorize]
public class AppRunnerBackendFilesController : ControllerBase
{
    private readonly ApplicationDbContext dataContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly UserContextService userContextService;
    private readonly IHubContext<AutomatonHub> automatonHub;

    public AppRunnerBackendFilesController(
        ApplicationDbContext dataContext,
        ResourceAuthorizationService authorizationService,
        UserContextService userContextService,
        IHubContext<AutomatonHub> automatonHub)
    {
        this.dataContext = dataContext;
        this.authorizationService = authorizationService;
        this.userContextService = userContextService;
        this.automatonHub = automatonHub;
    }

    [HttpGet]
    public async Task<ActionResult<AppBackendFilesResponse>> List(Guid appId, CancellationToken cancellationToken)
    {
        var (runner, error) = await ResolveRunnerAsync(appId, cancellationToken);
        if (error != null)
        {
            return error;
        }

        var response = await automatonHub.Clients.Client(runner!.ConnectionId!)
            .InvokeAsync<AppBackendFilesResponse>(
                AutomatonHubMethods.ListAppBackendFiles,
                appId.ToString(),
                cancellationToken);

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpGet("{*path}")]
    public async Task<ActionResult<AppBackendFileContentResponse>> Read(
        Guid appId,
        string path,
        CancellationToken cancellationToken)
    {
        var (runner, error) = await ResolveRunnerAsync(appId, cancellationToken);
        if (error != null)
        {
            return error;
        }

        var response = await automatonHub.Clients.Client(runner!.ConnectionId!)
            .InvokeAsync<AppBackendFileContentResponse>(
                AutomatonHubMethods.ReadAppBackendFile,
                appId.ToString(),
                path,
                cancellationToken);

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpDelete("{*path}")]
    public async Task<ActionResult<AppBackendFileDeleteResponse>> Delete(
        Guid appId,
        string path,
        CancellationToken cancellationToken)
    {
        var (runner, error) = await ResolveRunnerAsync(appId, cancellationToken);
        if (error != null)
        {
            return error;
        }

        var response = await automatonHub.Clients.Client(runner!.ConnectionId!)
            .InvokeAsync<AppBackendFileDeleteResponse>(
                AutomatonHubMethods.DeleteAppBackendFile,
                appId.ToString(),
                path,
                cancellationToken);

        return response.Success ? Ok(response) : BadRequest(response);
    }

    private async Task<(Runner? Runner, ActionResult? Error)> ResolveRunnerAsync(Guid appId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanAccessAppAsync(appId, cancellationToken))
        {
            return (null, NotFound());
        }

        var userId = userContextService.GetUserId();
        var runner = await dataContext.WebApps
            .Where(app => app.Id == appId)
            .Select(app => app.ExecutionRunnerId == null
                ? null
                : dataContext.Runners
                    .Where(r => r.Id == app.ExecutionRunnerId)
                    .Where(r => r.RunnerUsers.Any(ru => ru.UserId == userId))
                    .FirstOrDefault())
            .FirstOrDefaultAsync(cancellationToken);

        if (runner == null)
        {
            return (null, BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Error = "No accessible runner is configured for this app."
            }));
        }

        if (string.IsNullOrWhiteSpace(runner.ConnectionId))
        {
            return (null, BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' is not connected."
            }));
        }

        return (runner, null);
    }
}
