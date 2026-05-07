using Automaton.Cli.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Automaton.Cli.Services;

public class LocalAppExecutionService(PythonRuntimeService pythonRuntimeService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<string> DiscoverControllers(LocalAppContext context)
    {
        if (!Directory.Exists(context.BackendPath))
        {
            return [];
        }

        return Directory.GetFiles(context.BackendPath, "*.py", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "__init__", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public async Task<LocalAppExecutionResult> ExecuteAsync(LocalAppContext context, LocalAppExecutionOptions options, CancellationToken cancellationToken = default)
    {
        JsonDocument.Parse(options.InputsJson).Dispose();

        if (string.Equals(options.Mode, LocalAppExecutionModes.MockOnly, StringComparison.Ordinal))
        {
            return await LoadMockAsync(context, options, cancellationToken);
        }

        var controllerPath = GetControllerPath(context, options.ControllerName);
        if (!File.Exists(controllerPath))
        {
            if (string.Equals(options.Mode, LocalAppExecutionModes.MockFallback, StringComparison.Ordinal))
            {
                return await LoadMockAsync(context, options, cancellationToken);
            }

            return new LocalAppExecutionResult
            {
                ControllerName = options.ControllerName,
                ActionName = options.ActionName,
                Mode = options.Mode,
                Duration = TimeSpan.Zero,
                Success = false,
                Data = null,
                Error = $"Controller '{options.ControllerName}' not found.",
                FailureReason = LocalAppFailureReasons.ControllerNotFound,
                ControllerPath = controllerPath
            };
        }

        var runtime = await pythonRuntimeService.ResolveAsync(context.RootPath, cancellationToken);
        return await ExecutePythonAsync(context, controllerPath, runtime, options, cancellationToken);
    }

    public async Task<LocalAppValidationCheck> InspectControllerAsync(LocalAppContext context, string controllerName, CancellationToken cancellationToken = default)
    {
        var controllerPath = GetControllerPath(context, controllerName);
        if (!File.Exists(controllerPath))
        {
            return new LocalAppValidationCheck
            {
                Name = $"controller:{controllerName}",
                Status = LocalAppValidationStatuses.Fail,
                Message = $"Controller file not found: {controllerPath}"
            };
        }

        var runtime = await pythonRuntimeService.ResolveAsync(context.RootPath, cancellationToken);
        var request = JsonSerializer.Serialize(new
        {
            operation = "inspect",
            projectRoot = context.RootPath,
            controllerName,
            controllerPath
        }, JsonOptions);

        var start = Stopwatch.StartNew();
        var payload = await RunPythonProcessAsync(runtime, context.RootPath, request, TimeSpan.FromSeconds(10), cancellationToken);
        start.Stop();

        if (!payload.TimedOut && payload.Document is not null && payload.Document.RootElement.TryGetProperty("ok", out var okProperty) && okProperty.GetBoolean())
        {
            return new LocalAppValidationCheck
            {
                Name = $"controller:{controllerName}",
                Status = LocalAppValidationStatuses.Pass,
                Message = $"handle() available ({start.ElapsedMilliseconds} ms)"
            };
        }

        var message = ReadString(payload.Document, "error") ?? payload.ProcessError ?? $"Controller validation failed after {start.ElapsedMilliseconds} ms.";
        return new LocalAppValidationCheck
        {
            Name = $"controller:{controllerName}",
            Status = LocalAppValidationStatuses.Fail,
            Message = message
        };
    }

    private async Task<LocalAppExecutionResult> LoadMockAsync(LocalAppContext context, LocalAppExecutionOptions options, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var mockPath = Path.Combine(context.MocksPath, $"{options.ControllerName}.{options.ActionName}.json");
        if (!File.Exists(mockPath))
        {
            return new LocalAppExecutionResult
            {
                ControllerName = options.ControllerName,
                ActionName = options.ActionName,
                Mode = options.Mode,
                Duration = start.Elapsed,
                Success = false,
                Data = null,
                Error = $"Mock file not found: {mockPath}",
                FailureReason = LocalAppFailureReasons.MockNotFound,
                MockPath = mockPath
            };
        }

        await using var stream = File.OpenRead(mockPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        start.Stop();

        return CreateResultFromResponse(document.RootElement, options.ControllerName, options.ActionName, options.Mode, start.Elapsed, mockPath, controllerPath: null, pythonRuntime: null);
    }

    private async Task<LocalAppExecutionResult> ExecutePythonAsync(LocalAppContext context, string controllerPath, PythonRuntimeInfo runtime, LocalAppExecutionOptions options, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(new
        {
            operation = "invoke",
            projectRoot = context.RootPath,
            controllerName = options.ControllerName,
            controllerPath,
            actionName = options.ActionName,
            inputs = JsonSerializer.Deserialize<object?>(options.InputsJson, JsonOptions)
        }, JsonOptions);

        var start = Stopwatch.StartNew();
        var payload = await RunPythonProcessAsync(runtime, context.RootPath, request, options.Timeout, cancellationToken);
        start.Stop();

        if (payload.TimedOut)
        {
            return new LocalAppExecutionResult
            {
                ControllerName = options.ControllerName,
                ActionName = options.ActionName,
                Mode = options.Mode,
                Duration = start.Elapsed,
                Success = false,
                Data = null,
                Error = $"Controller timed out after {options.Timeout.TotalSeconds:F0} second(s).",
                FailureReason = LocalAppFailureReasons.Timeout,
                ControllerPath = controllerPath,
                PythonRuntime = runtime
            };
        }

        if (payload.Document is null)
        {
            return new LocalAppExecutionResult
            {
                ControllerName = options.ControllerName,
                ActionName = options.ActionName,
                Mode = options.Mode,
                Duration = start.Elapsed,
                Success = false,
                Data = null,
                Error = payload.ProcessError ?? "Python execution returned no result.",
                FailureReason = LocalAppFailureReasons.RuntimeException,
                ControllerPath = controllerPath,
                PythonRuntime = runtime,
                StdErr = payload.ProcessError
            };
        }

        if (!payload.Document.RootElement.TryGetProperty("ok", out var okProperty) || !okProperty.GetBoolean())
        {
            var failureReason = ReadString(payload.Document, "errorType") ?? LocalAppFailureReasons.RuntimeException;
            return new LocalAppExecutionResult
            {
                ControllerName = options.ControllerName,
                ActionName = options.ActionName,
                Mode = options.Mode,
                Duration = start.Elapsed,
                Success = false,
                Data = null,
                Error = ReadString(payload.Document, "error") ?? payload.ProcessError ?? "Controller execution failed.",
                FailureReason = failureReason,
                Traceback = ReadString(payload.Document, "traceback"),
                StdOut = ReadString(payload.Document, "stdout"),
                StdErr = MergeText(ReadString(payload.Document, "stderr"), payload.ProcessError),
                ControllerPath = controllerPath,
                PythonRuntime = runtime
            };
        }

        if (!payload.Document.RootElement.TryGetProperty("response", out var responseProperty))
        {
            return new LocalAppExecutionResult
            {
                ControllerName = options.ControllerName,
                ActionName = options.ActionName,
                Mode = options.Mode,
                Duration = start.Elapsed,
                Success = false,
                Data = null,
                Error = "Controller returned no response payload.",
                FailureReason = LocalAppFailureReasons.MalformedResponse,
                ControllerPath = controllerPath,
                PythonRuntime = runtime,
                StdOut = ReadString(payload.Document, "stdout"),
                StdErr = MergeText(ReadString(payload.Document, "stderr"), payload.ProcessError)
            };
        }

        var result = CreateResultFromResponse(responseProperty, options.ControllerName, options.ActionName, options.Mode, start.Elapsed, mockPath: null, controllerPath, runtime);
        result.StdOut = ReadString(payload.Document, "stdout");
        result.StdErr = MergeText(ReadString(payload.Document, "stderr"), payload.ProcessError);
        result.Traceback = ReadString(payload.Document, "traceback");
        return result;
    }

    private static LocalAppExecutionResult CreateResultFromResponse(JsonElement response, string controllerName, string actionName, string mode, TimeSpan duration, string? mockPath, string? controllerPath, PythonRuntimeInfo? pythonRuntime)
    {
        if (!TryReadStandardResponse(response, out var success, out var data, out var error, out var malformedMessage))
        {
            return new LocalAppExecutionResult
            {
                ControllerName = controllerName,
                ActionName = actionName,
                Mode = mode,
                Duration = duration,
                Success = false,
                Data = null,
                Error = malformedMessage,
                FailureReason = LocalAppFailureReasons.MalformedResponse,
                MockPath = mockPath,
                ControllerPath = controllerPath,
                PythonRuntime = pythonRuntime
            };
        }

        return new LocalAppExecutionResult
        {
            ControllerName = controllerName,
            ActionName = actionName,
            Mode = mode,
            Duration = duration,
            Success = success,
            Data = data,
            Error = error,
            MockPath = mockPath,
            ControllerPath = controllerPath,
            PythonRuntime = pythonRuntime
        };
    }

    private static bool TryReadStandardResponse(JsonElement response, out bool success, out object? data, out string error, out string malformedMessage)
    {
        success = false;
        data = null;
        error = string.Empty;
        malformedMessage = string.Empty;

        if (response.ValueKind != JsonValueKind.Object)
        {
            malformedMessage = "Response must be a JSON object shaped like { success, data, error }.";
            return false;
        }

        if (!response.TryGetProperty("success", out var successProperty) || successProperty.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            malformedMessage = "Response is missing boolean 'success'.";
            return false;
        }

        if (!response.TryGetProperty("data", out var dataProperty))
        {
            malformedMessage = "Response is missing 'data'.";
            return false;
        }

        if (!response.TryGetProperty("error", out var errorProperty) || errorProperty.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
        {
            malformedMessage = "Response is missing string 'error'.";
            return false;
        }

        success = successProperty.GetBoolean();
        data = dataProperty.ValueKind == JsonValueKind.Null ? null : dataProperty.Clone();
        error = errorProperty.ValueKind == JsonValueKind.Null ? string.Empty : errorProperty.GetString() ?? string.Empty;
        return true;
    }

    private async Task<PythonProcessPayload> RunPythonProcessAsync(PythonRuntimeInfo runtime, string workingDirectory, string requestJson, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "local_app_runtime.py");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Python runtime helper not found: {scriptPath}");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(runtime.ExecutablePath, QuoteArgument(scriptPath))
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;
            JsonDocument? document = null;

            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                try
                {
                    document = JsonDocument.Parse(stdOut);
                }
                catch (JsonException)
                {
                    stdErr = MergeText(stdErr, stdOut);
                }
            }

            return new PythonProcessPayload
            {
                TimedOut = false,
                Document = document,
                ProcessError = string.IsNullOrWhiteSpace(stdErr) ? null : stdErr.Trim()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore best-effort termination failures.
            }

            return new PythonProcessPayload { TimedOut = true };
        }
    }

    private static string QuoteArgument(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }

    private static string GetControllerPath(LocalAppContext context, string controllerName)
    {
        return Path.Combine(context.BackendPath, controllerName + ".py");
    }

    private static string? ReadString(JsonDocument? document, string propertyName)
    {
        if (document is null)
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static string? MergeText(string? first, string? second)
    {
        var parts = new[] { first?.Trim(), second?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private sealed class PythonProcessPayload
    {
        public bool TimedOut { get; init; }
        public JsonDocument? Document { get; init; }
        public string? ProcessError { get; init; }
    }
}
