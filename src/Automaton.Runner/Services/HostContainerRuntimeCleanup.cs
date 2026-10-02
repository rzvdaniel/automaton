using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostContainerRuntimeCleanup(
    Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> executor,
    Func<HostContainerExecutionRequest, CancellationToken, Task<HostContainerExecutionResponse>> remove)
{
    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request, IReadOnlyDictionary<string, string> labels, CancellationToken token)
    {
        var arguments = new List<string> { "docker", "ps", "--all", "--quiet", "--no-trunc" };
        foreach (var label in labels)
        {
            arguments.Add("--filter");
            arguments.Add($"label={label.Key}={label.Value}");
        }
        var listed = await executor(arguments, TimeSpan.FromMinutes(2), token);
        if (!listed.Success) return Failure("container_list", "Docker could not list owned runtime containers.");
        foreach (var id in listed.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!HostContainerRuntimeValidation.IsContainerId(id))
                return Failure("container_list", "Docker returned an invalid container identity.");
            var removed = await remove(request with
            { Operation = HostContainerOperationKinds.RemoveContainer, ContainerId = id }, token);
            if (!removed.Success) return removed;
        }
        return new() { Success = true };
    }
    private static HostContainerExecutionResponse Failure(string code, string message)
        => new() { ErrorCode = code, Error = message };
}
