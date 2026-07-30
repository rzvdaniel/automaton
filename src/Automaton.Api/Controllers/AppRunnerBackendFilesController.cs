using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/runner-backend/files")]
[Authorize]
public class AppRunnerBackendFilesController : ControllerBase
{
    private readonly ApplicationDbContext dataContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly UserContextService userContextService;
    private readonly AppFileStorageService fileStorageService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly ILogger<AppRunnerBackendFilesController> logger;

    public AppRunnerBackendFilesController(
        ApplicationDbContext dataContext,
        ResourceAuthorizationService authorizationService,
        UserContextService userContextService,
        AppFileStorageService fileStorageService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        IHubContext<AutomatonHub> automatonHub,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        ILogger<AppRunnerBackendFilesController> logger)
    {
        this.dataContext = dataContext;
        this.authorizationService = authorizationService;
        this.userContextService = userContextService;
        this.fileStorageService = fileStorageService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.automatonHub = automatonHub;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AppBackendFilesResponse>> List(
        Guid appId,
        [FromQuery] string? area,
        CancellationToken cancellationToken)
    {
        var runnerFileArea = NormalizeArea(area);
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppBackendFilesResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = ConfirmedEmailGuardService.SensitiveActionMessage
            });
        }

        if (!await authorizationService.CanAccessAppAsync(appId, cancellationToken))
        {
            return NotFound();
        }

        var hasRuntimeFiles = await HasRuntimeFilesAsync(appId, runnerFileArea, cancellationToken);
        if (!hasRuntimeFiles)
        {
            return Ok(new AppBackendFilesResponse
            {
                Success = true,
                Area = runnerFileArea
            });
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
                    runnerFileArea,
                    cancellationToken);
        }
        catch (HubException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} failed to list backend files for app {AppId}", runner!.Id, appId);
            return BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = BuildRunnerErrorMessage(runner.Name, runnerFileArea)
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is stale while listing backend files for app {AppId}", runner!.Id, appId);
            return BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then try again."
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is invalid while listing backend files for app {AppId}", runner!.Id, appId);
            return BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then try again."
            });
        }

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpGet("{*path}")]
    public async Task<ActionResult<AppBackendFileContentResponse>> Read(
        Guid appId,
        string path,
        [FromQuery] string? area,
        CancellationToken cancellationToken)
    {
        var runnerFileArea = NormalizeArea(area);
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppBackendFileContentResponse
            {
                Success = false,
                Area = runnerFileArea,
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
                    runnerFileArea,
                    cancellationToken);
        }
        catch (HubException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} failed to read backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileContentResponse
            {
                Success = false,
                Area = runnerFileArea,
                Path = path,
                Error = BuildRunnerErrorMessage(runner.Name, runnerFileArea)
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is stale while reading backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileContentResponse
            {
                Success = false,
                Area = runnerFileArea,
                Path = path,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then try again."
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is invalid while reading backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileContentResponse
            {
                Success = false,
                Area = runnerFileArea,
                Path = path,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then try again."
            });
        }

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpDelete("{*path}")]
    public async Task<ActionResult<AppBackendFileDeleteResponse>> Delete(
        Guid appId,
        string path,
        [FromQuery] string? area,
        CancellationToken cancellationToken)
    {
        var runnerFileArea = NormalizeArea(area);
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppBackendFileDeleteResponse
            {
                Success = false,
                Area = runnerFileArea,
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
                    runnerFileArea,
                    cancellationToken);
        }
        catch (HubException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} failed to delete backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileDeleteResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = BuildRunnerErrorMessage(runner.Name, runnerFileArea)
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is stale while deleting backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileDeleteResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then try again."
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Runner {RunnerId} connection is invalid while deleting backend file {Path} for app {AppId}", runner!.Id, path, appId);
            return BadRequest(new AppBackendFileDeleteResponse
            {
                Success = false,
                Area = runnerFileArea,
                Error = $"Runner '{runner.Name}' is no longer connected. Reconnect the runner, then try again."
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

        try
        {
            var runner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
                appId,
                userContextService.GetUserId(),
                requireOnline: true,
                cancellationToken: cancellationToken);
            return (runner, null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Area = AppRunnerFileAreas.Backend,
                Error = ex.Message
            }));
        }
        catch (UnauthorizedAccessException)
        {
            return (null, BadRequest(new AppBackendFilesResponse
            {
                Success = false,
                Area = AppRunnerFileAreas.Backend,
                Error = "No accessible runner is configured for this app."
            }));
        }
    }

    private static string NormalizeArea(string? area)
    {
        return string.Equals(area, AppRunnerFileAreas.Data, StringComparison.OrdinalIgnoreCase)
            ? AppRunnerFileAreas.Data
            : AppRunnerFileAreas.Backend;
    }

    private static string BuildRunnerErrorMessage(string runnerName, string area)
    {
        var areaLabel = area == AppRunnerFileAreas.Data ? "data files" : "backend files";
        return $"Runner '{runnerName}' could not access {areaLabel}. Update or reconnect the runner, then try again.";
    }

    private async Task<bool> HasRuntimeFilesAsync(Guid appId, string area, CancellationToken cancellationToken)
    {
        if (area == AppRunnerFileAreas.Data)
        {
            return true;
        }

        var ownerId = await ResolveAppOwnerIdAsync(appId, cancellationToken);
        var liveBackendFiles = await fileStorageService.GetRuntimeBackendFilesAsync(ownerId, appId);
        return liveBackendFiles.Any();
    }
}
