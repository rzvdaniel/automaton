using Automaton.Core.Models;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/runner-backend/files")]
[Authorize]
public class AppRunnerBackendFilesController : ControllerBase
{
    private readonly ApplicationDbContext dataContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly UserContextService userContextService;
    private readonly AppFileStorageService fileStorageService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly ILogger<AppRunnerBackendFilesController> logger;

    public AppRunnerBackendFilesController(
        ApplicationDbContext dataContext,
        ResourceAuthorizationService authorizationService,
        UserContextService userContextService,
        AppFileStorageService fileStorageService,
        IHubContext<AutomatonHub> automatonHub,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        ILogger<AppRunnerBackendFilesController> logger)
    {
        this.dataContext = dataContext;
        this.authorizationService = authorizationService;
        this.userContextService = userContextService;
        this.fileStorageService = fileStorageService;
        this.automatonHub = automatonHub;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AppBackendFilesResponse>> List(Guid appId, CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppBackendFilesResponse
            {
                Success = false,
                Error = ConfirmedEmailGuardService.SensitiveActionMessage
            });
        }

        if (!await authorizationService.CanAccessAppAsync(appId, cancellationToken))
        {
            return NotFound();
        }

        var ownerId = await ResolveAppOwnerIdAsync(appId, cancellationToken);
        var liveBackendFiles = await fileStorageService.GetRuntimeBackendFilesAsync(ownerId, appId);
        if (!liveBackendFiles.Any())
        {
            return Ok(new AppBackendFilesResponse { Success = true });
        }

        var (runner, error) = await ResolveRunnerAsync(appId, cancellationToken);
        if (error != null)
        {
            return error;
        }

        AppBackendFilesResponse response;
        try
        {
            response = await automatonHub.Clients.Client(runner!.ConnectionId!)
                .InvokeAsync<AppBackendFilesResponse>(
                    AutomatonHubMethods.ListAppBackendFiles,
                    appId.ToString(),
                    cancellationToken);
        }
        catch (HubException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} failed to list backend files for app {AppId}", runner!.Id, appId);
            return BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' could not list backend files. Update or reconnect the runner, then redeploy the backend."
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is stale while listing backend files for app {AppId}", runner!.Id, appId);
            return BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then redeploy the backend."
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is invalid while listing backend files for app {AppId}", runner!.Id, appId);
            return BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then redeploy the backend."
            });
        }

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpGet("{*path}")]
    public async Task<ActionResult<AppBackendFileContentResponse>> Read(
        Guid appId,
        string path,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppBackendFileContentResponse
            {
                Success = false,
                Path = path,
                Error = ConfirmedEmailGuardService.SensitiveActionMessage
            });
        }

        var (runner, error) = await ResolveRunnerAsync(appId, cancellationToken);
        if (error != null)
        {
            return error;
        }

        AppBackendFileContentResponse response;
        try
        {
            response = await automatonHub.Clients.Client(runner!.ConnectionId!)
                .InvokeAsync<AppBackendFileContentResponse>(
                    AutomatonHubMethods.ReadAppBackendFile,
                    appId.ToString(),
                    path,
                    cancellationToken);
        }
        catch (HubException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} failed to read backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileContentResponse
            {
                Success = false,
                Path = path,
                Error = $"Runner '{runner.Name}' could not read this backend file. Update or reconnect the runner, then redeploy the backend."
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is stale while reading backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileContentResponse
            {
                Success = false,
                Path = path,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then redeploy the backend."
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is invalid while reading backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileContentResponse
            {
                Success = false,
                Path = path,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then redeploy the backend."
            });
        }

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpDelete("{*path}")]
    public async Task<ActionResult<AppBackendFileDeleteResponse>> Delete(
        Guid appId,
        string path,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppBackendFileDeleteResponse
            {
                Success = false,
                Error = ConfirmedEmailGuardService.SensitiveActionMessage
            });
        }

        var (runner, error) = await ResolveRunnerAsync(appId, cancellationToken);
        if (error != null)
        {
            return error;
        }

        AppBackendFileDeleteResponse response;
        try
        {
            response = await automatonHub.Clients.Client(runner!.ConnectionId!)
                .InvokeAsync<AppBackendFileDeleteResponse>(
                    AutomatonHubMethods.DeleteAppBackendFile,
                    appId.ToString(),
                    path,
                    cancellationToken);
        }
        catch (HubException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} failed to delete backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileDeleteResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' could not delete this backend file. Update or reconnect the runner, then redeploy the backend."
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is stale while deleting backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileDeleteResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then redeploy the backend."
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is invalid while deleting backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileDeleteResponse
            {
                Success = false,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then redeploy the backend."
            });
        }

        return response.Success ? Ok(response) : BadRequest(response);
    }

    private async Task<Guid> ResolveAppOwnerIdAsync(Guid appId, CancellationToken cancellationToken)
    {
        var ownerId = await dataContext.WebAppUsers
            .Where(webAppUser => webAppUser.WebAppId == appId)
            .Select(webAppUser => (Guid?)webAppUser.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return ownerId ?? userContextService.GetUserId();
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
