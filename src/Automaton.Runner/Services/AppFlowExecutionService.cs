using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Automaton.Runner.Console.Services;

public class AppFlowExecutionService
{
    private const string FlowArtifactsDirectoryName = ".automaton";
    private const string FlowArtifactsFolderName = "flows";
    private const string FlowIndexFileName = "index.json";

    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly FlowExecuteService flowExecuteService;
    private readonly ConfigurationService configurationService;

    public AppFlowExecutionService(
        RunnerWorkspaceService runnerWorkspaceService,
        FlowExecuteService flowExecuteService,
        ConfigurationService configurationService)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.flowExecuteService = flowExecuteService;
        this.configurationService = configurationService;
    }

    public async Task<AppFlowExecutionResult> ExecuteAsync(
        Guid appId,
        string flowIdentifier,
        IDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        EnsureAppExists(appId);

        var index = await LoadFlowIndexAsync(appId, cancellationToken);
        var entry = ResolveFlow(index, flowIdentifier);
        if (entry == null)
        {
            return new AppFlowExecutionResult
            {
                Success = false,
                Status = WorkflowStatus.None.ToString(),
                RunnerCount = 1,
                ExecutedAt = DateTime.UtcNow,
                Message = $"Flow '{flowIdentifier}' not found"
            };
        }

        var flow = await LoadFlowAsync(appId, entry.FlowId, cancellationToken);
        ApplyInputVariables(flow, inputs);

        var execution = await flowExecuteService.Execute(flow, cancellationToken);
        return new AppFlowExecutionResult
        {
            Success = execution.Status == WorkflowStatus.Success,
            FlowId = execution.FlowId,
            Status = execution.Status.ToString(),
            RunnerCount = 1,
            ExecutedAt = execution.Finished == default ? DateTime.UtcNow : execution.Finished,
            Message = execution.Status == WorkflowStatus.Success
                ? "Flow executed successfully"
                : "Flow execution failed or incomplete",
            OutputVariables = execution.OutputVariables,
            Logs = execution.Logs.ToList()
        };
    }

    private void EnsureAppExists(Guid appId)
    {
        var appRoot = runnerWorkspaceService.GetAppRoot(appId);
        if (!Directory.Exists(appRoot))
        {
            throw new DirectoryNotFoundException($"unknown app '{appId}'");
        }
    }

    private async Task<List<FlowIndexEntry>> LoadFlowIndexAsync(Guid appId, CancellationToken cancellationToken)
    {
        var indexPath = GetFlowIndexPath(appId);
        if (!File.Exists(indexPath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(indexPath, cancellationToken);
        return JsonSerializer.Deserialize<List<FlowIndexEntry>>(json, JsonOptions()) ?? [];
    }

    private async Task<Flow> LoadFlowAsync(Guid appId, Guid flowId, CancellationToken cancellationToken)
    {
        var flowPath = GetFlowArtifactPath(appId, flowId);
        if (!File.Exists(flowPath))
        {
            throw new FileNotFoundException($"Flow artifact '{flowId}' not found.", flowPath);
        }

        var json = await File.ReadAllTextAsync(flowPath, cancellationToken);
        var flow = JsonSerializer.Deserialize<Flow>(json, JsonOptions());
        if (flow == null)
        {
            throw new InvalidOperationException($"Flow artifact '{flowId}' is invalid.");
        }

        return flow;
    }

    private FlowIndexEntry? ResolveFlow(IEnumerable<FlowIndexEntry> entries, string flowIdentifier)
    {
        if (Guid.TryParse(flowIdentifier, out var flowId))
        {
            return entries.FirstOrDefault(entry => entry.FlowId == flowId);
        }

        return entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, flowIdentifier, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyInputVariables(Flow flow, IDictionary<string, string> inputs)
    {
        foreach (var input in inputs)
        {
            var matchingVar = flow.InputVariables.Values
                .FirstOrDefault(v => string.Equals(v.Name, input.Key, StringComparison.OrdinalIgnoreCase));

            if (matchingVar != null)
            {
                matchingVar.Value = input.Value;
            }
        }
    }

    private string GetFlowIndexPath(Guid appId)
        => Path.Combine(GetFlowArtifactsDirectory(appId), FlowIndexFileName);

    private string GetFlowArtifactPath(Guid appId, Guid flowId)
        => Path.Combine(GetFlowArtifactsDirectory(appId), $"{flowId:N}.json");

    private string GetFlowArtifactsDirectory(Guid appId)
        => Path.Combine(
            runnerWorkspaceService.GetAppBackendDirectory(appId),
            FlowArtifactsDirectoryName,
            FlowArtifactsFolderName);

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true
        };
}

public sealed class AppFlowExecutionResult
{
    public bool Success { get; set; }
    public Guid FlowId { get; set; }
    public string Status { get; set; } = WorkflowStatus.None.ToString();
    public int RunnerCount { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public IDictionary<string, StepVariable> OutputVariables { get; set; } = new Dictionary<string, StepVariable>();
    public List<ExecutionLogEntry> Logs { get; set; } = [];
}

public sealed class FlowIndexEntry
{
    public Guid FlowId { get; set; }
    public string Name { get; set; } = string.Empty;
}
