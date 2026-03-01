using Serilog;
using Automaton.Core.Scripting.Step;
using Automaton.Core.Scripting.Engines;
using Automaton.Runner.Console.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Handler for executing app controller actions on the runner.
/// Scripts must be deployed to the runner first (via UploadControllerScript).
/// Reads the script from local disk, injects action_name + inputs, and executes it.
/// </summary>
public class AppControllerExecuteService
{
    private const string BackendFolder = "backend";
    private static readonly string[] SupportedExtensions = [".py", ".bat", ".txt"];

    private readonly CustomStepExecutor executor;
    private readonly ILogger logger;

    public AppControllerExecuteService(ScriptEngineHost scriptHost)
    {
        this.executor = new CustomStepExecutor(scriptHost);
        this.logger = Log.ForContext<AppControllerExecuteService>();
    }

    public async Task<AppControllerExecutionResponse> RunControllerAction(AppControllerPayload payload)
    {
        logger.Information("Executing controller action {ControllerName}.{ActionName} for app {AppId}",
            payload.ControllerName, payload.ActionName, payload.AppId);

        System.Console.WriteLine($"▶️  Executing controller action: {payload.ControllerName}.{payload.ActionName}");

        try
        {
            // Load script from local persistent volume — code is NOT in the payload
            var (code, language, scriptPath) = FindLocalScript(payload.AppId, payload.ControllerName)
                ?? throw new InvalidOperationException(
                    $"Controller '{payload.ControllerName}' not deployed to this runner. " +
                    $"Use the 'Deploy Backend' button in the app editor to push scripts to this runner.");

            // Inject action_name so the controller code knows which action to run.
            // Also inject __file__ so Python scripts can use os.path.dirname(__file__) for relative paths.
            // _inputs_json is the JSON-encoded user inputs, decoded into `inputs` dict by the preamble below.
            var userInputs = payload.Inputs ?? new Dictionary<string, string>();
            var inputs = new Dictionary<string, string>(userInputs)
            {
                ["action_name"] = payload.ActionName,
                ["__file__"]    = scriptPath,
                ["_inputs_json"] = System.Text.Json.JsonSerializer.Serialize(userInputs)
            };

            // Prepend a preamble that builds the `inputs` Python dict from the injected JSON.
            // This lets AI-generated scripts use inputs.get('key') naturally.
            const string inputsPreamble = "import json as _json; inputs = _json.loads(_inputs_json)\n";
            var codeWithPreamble = inputsPreamble + code;

            var result = await Task.Run(() =>
                executor.Execute(codeWithPreamble, language, inputs, payload.OutputVariableNames));

            if (!result.Success)
            {
                logger.Warning("Controller action {ControllerName}.{ActionName} failed: {Error}",
                    payload.ControllerName, payload.ActionName, result.Error);

                System.Console.WriteLine($"❌ Controller action failed: {payload.ControllerName}.{payload.ActionName}");

                return new AppControllerExecutionResponse
                {
                    Success = false,
                    Output = result.Output,
                    Error = result.Error,
                    Started = result.Started,
                    Finished = result.Finished,
                    OutputVariables = result.OutputVariables ?? []
                };
            }

            logger.Information("Controller action {ControllerName}.{ActionName} completed in {ElapsedMs}ms",
                payload.ControllerName, payload.ActionName, result.ExecutionTimeMs);

            System.Console.WriteLine($"✅ Controller action completed: {payload.ControllerName}.{payload.ActionName} ({result.ExecutionTimeMs}ms)");

            return new AppControllerExecutionResponse
            {
                Success = true,
                Output = result.Output,
                Error = null,
                Started = result.Started,
                Finished = result.Finished,
                OutputVariables = NormalizeOutputVariables(result.OutputVariables ?? [])
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing controller action {ControllerName}.{ActionName}",
                payload.ControllerName, payload.ActionName);

            System.Console.WriteLine($"❌ Controller action exception: {payload.ControllerName}.{payload.ActionName} - {ex.Message}");

            return new AppControllerExecutionResponse
            {
                Success = false,
                Output = string.Empty,
                Error = $"Execution exception: {ex.Message}",
                Started = DateTime.UtcNow,
                Finished = DateTime.UtcNow,
                OutputVariables = []
            };
        }
    }

    /// <summary>
    /// Finds a deployed controller script on the local filesystem.
    /// Path: {workingDir}/data/apps/{appId}/backend/{controllerName}.{ext}
    /// </summary>
    private static (string Code, string Language, string Path)? FindLocalScript(Guid appId, string controllerName)
    {
        var backendPath = GetBackendPath(appId);
        if (!Directory.Exists(backendPath)) return null;

        foreach (var ext in SupportedExtensions)
        {
            var filePath = Path.Combine(backendPath, controllerName + ext);
            if (File.Exists(filePath))
            {
                var code = File.ReadAllText(filePath);
                var language = GetLanguageFromExtension(ext);
                return (code, language, filePath);
            }
        }
        return null;
    }

    /// <summary>Writes a controller script to local disk (called by upload handler).</summary>
    public static void SaveScript(Guid appId, string controllerName, string code, string language)
    {
        var backendPath = GetBackendPath(appId);
        Directory.CreateDirectory(backendPath);
        var ext = GetExtensionFromLanguage(language);
        File.WriteAllText(Path.Combine(backendPath, controllerName + ext), code);
    }

    private static string GetBackendPath(Guid appId) =>
        Path.Combine(AppContext.BaseDirectory, "data", "apps", appId.ToString(), BackendFolder);

    private static string GetLanguageFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".py"  => "Python",
        ".bat" => "Batch",
        _      => "Text"
    };

    private static string GetExtensionFromLanguage(string language) => language.ToLowerInvariant() switch
    {
        "python" => ".py",
        "batch"  => ".bat",
        _        => ".txt"
    };

    /// <summary>
    /// Normalizes the 'result' output variable to avoid double JSON-encoding.
    /// ConvertFromPython calls json.dumps on every Python value. If AI code also calls
    /// json.dumps before assigning to result, the value arrives as a JSON-encoded string
    /// (e.g. '"[{...}]"') instead of the raw JSON value ('[{...}]').
    /// This method unwraps one level when result is a quoted JSON string that itself
    /// contains a valid JSON array or object.
    /// </summary>
    private static Dictionary<string, string> NormalizeOutputVariables(IDictionary<string, string> vars)
    {
        var normalized = new Dictionary<string, string>(vars);
        if (normalized.TryGetValue("result", out var raw) && raw != null)
        {
            normalized["result"] = UnwrapDoubleEncodedJson(raw);
        }
        return normalized;
    }

    private static string UnwrapDoubleEncodedJson(string value)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(value);
            // If the outer JSON is a string, check if its content is also valid JSON
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var inner = doc.RootElement.GetString();
                if (inner != null)
                {
                    var trimmed = inner.TrimStart();
                    if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
                    {
                        try
                        {
                            System.Text.Json.JsonDocument.Parse(inner);
                            return inner; // Unwrap one level
                        }
                        catch { /* inner is not valid JSON; keep outer */ }
                    }
                }
            }
        }
        catch { /* value is not valid JSON at all; return as-is */ }
        return value;
    }
}
