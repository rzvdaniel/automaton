using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public class LocalAppValidationService(
    LocalAppProjectService projectService,
    LocalAppExecutionService executionService,
    PythonRuntimeService pythonRuntimeService)
{
    public async Task<LocalAppValidationResult> ValidateAsync(string? startDirectory = null, bool containerMode = false, CancellationToken cancellationToken = default)
    {
        var context = projectService.ResolveContext(startDirectory);
        var checks = new List<LocalAppValidationCheck>();

        checks.Add(CreateCheck(
            "automatonJson",
            context.Config is null ? LocalAppValidationStatuses.Warning : LocalAppValidationStatuses.Pass,
            context.Config is null
                ? "automaton.json not found — local development still works for unlinked apps."
                : "automaton.json found."));

        checks.Add(CreateCheck(
            "indexHtml",
            File.Exists(context.IndexHtmlPath) ? LocalAppValidationStatuses.Pass : LocalAppValidationStatuses.Fail,
            File.Exists(context.IndexHtmlPath)
                ? $"index.html found at {Path.GetRelativePath(context.RootPath, context.IndexHtmlPath)}"
                : $"index.html not found in {Path.GetRelativePath(context.RootPath, context.FrontendPath)}"));

        checks.Add(CreateCheck(
            "backendStructure",
            !context.HasBackendDirectory || Directory.Exists(context.BackendPath) ? LocalAppValidationStatuses.Pass : LocalAppValidationStatuses.Fail,
            context.HasBackendDirectory ? "backend/ directory found." : "No backend/ directory — frontend-only app."));

        var controllers = executionService.DiscoverControllers(context);
        checks.Add(CreateCheck(
            "controllerDiscovery",
            context.HasBackendDirectory && controllers.Count == 0 ? LocalAppValidationStatuses.Warning : LocalAppValidationStatuses.Pass,
            controllers.Count == 0
                ? (context.HasBackendDirectory ? "No top-level controllers discovered in backend/." : "No backend controllers to discover.")
                : $"{controllers.Count} controller(s) discovered: {string.Join(", ", controllers)}"));

        checks.Add(CreateCheck(
            "requirementsTxt",
            File.Exists(Path.Combine(context.BackendPath, "requirements.txt")) ? LocalAppValidationStatuses.Pass : LocalAppValidationStatuses.Warning,
            File.Exists(Path.Combine(context.BackendPath, "requirements.txt"))
                ? "backend/requirements.txt found."
                : "backend/requirements.txt not found."));

        var mockFiles = Directory.Exists(context.MocksPath)
            ? Directory.GetFiles(context.MocksPath, "*.json", SearchOption.TopDirectoryOnly).Length
            : 0;
        checks.Add(CreateCheck(
            "mocks",
            mockFiles > 0 ? LocalAppValidationStatuses.Pass : LocalAppValidationStatuses.Warning,
            mockFiles > 0 ? $"{mockFiles} mock file(s) discovered." : "No mock files discovered under backend/mocks/."));

        checks.Add(await ValidateDataDirectoryAsync(context, cancellationToken));

        if (!containerMode)
        {
            try
            {
                var runtime = await pythonRuntimeService.ResolveAsync(context.RootPath, cancellationToken);
                checks.Add(CreateCheck(
                    "python",
                    LocalAppValidationStatuses.Pass,
                    $"Using {runtime.ExecutablePath}{(string.IsNullOrWhiteSpace(runtime.Version) ? string.Empty : $" ({runtime.Version})")}"));

                foreach (var controller in controllers)
                {
                    checks.Add(await executionService.InspectControllerAsync(context, controller, cancellationToken));
                }
            }
            catch (Exception ex)
            {
                checks.Add(CreateCheck("python", LocalAppValidationStatuses.Fail, ex.Message));
            }
        }
        else
        {
            var dockerAvailable = await pythonRuntimeService.IsDockerAvailableAsync(cancellationToken);
            checks.Add(CreateCheck(
                "containerPrerequisites",
                dockerAvailable ? LocalAppValidationStatuses.Pass : LocalAppValidationStatuses.Fail,
                dockerAvailable ? "Docker CLI available." : "Docker CLI not found."));
        }

        return new LocalAppValidationResult
        {
            Context = context,
            Checks = checks
        };
    }

    private static async Task<LocalAppValidationCheck> ValidateDataDirectoryAsync(LocalAppContext context, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(context.DataPath);
            var probePath = Path.Combine(context.DataPath, ".write-test");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);

            return CreateCheck("dataDirectory", LocalAppValidationStatuses.Pass, $"Writable data directory: {Path.GetRelativePath(context.RootPath, context.DataPath)}");
        }
        catch (Exception ex)
        {
            return CreateCheck("dataDirectory", LocalAppValidationStatuses.Fail, ex.Message);
        }
    }

    private static LocalAppValidationCheck CreateCheck(string name, string status, string message)
    {
        return new LocalAppValidationCheck
        {
            Name = name,
            Status = status,
            Message = message
        };
    }
}
