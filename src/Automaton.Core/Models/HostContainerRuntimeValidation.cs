using System.Text.RegularExpressions;

namespace Automaton.Core.Models;

public static class HostContainerRuntimeValidation
{
    public static bool IsRuntimeOperation(string operation) => operation is
        HostContainerOperationKinds.CreateApplicationContainer or HostContainerOperationKinds.StartContainer
        or HostContainerOperationKinds.InspectContainer or HostContainerOperationKinds.GetContainerLogs
        or HostContainerOperationKinds.RemoveContainer;

    public static bool IsContainerReference(string? value) => value is not null
        && Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant);

    public static bool IsContainerId(string? value) => value is not null
        && Regex.IsMatch(value, @"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);

    public static bool IsRepoDigest(string? value) => value is not null
        && Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9._/:-]*@sha256:[a-f0-9]{64}\z", RegexOptions.CultureInvariant);

    public static string? Validate(HostContainerExecutionRequest request)
    {
        if (request.HostPort != 0)
            return "The host port is allocated by Docker, not the caller.";
        if (request.Environment is null)
            return "The environment must be an object.";

        if (request.Operation != HostContainerOperationKinds.CreateApplicationContainer)
        {
            if (!IsContainerReference(request.ContainerId))
                return "A valid container ID or name is required.";
            if (request.RequestedImage is not null || request.Command is not null || request.Storage is not null
                || request.Resources is not null || request.CandidateId != Guid.Empty
                || request.ContainerPort != 0 || request.Environment.Count != 0)
                return "Existing-container operations accept only scope and container identity.";
            return null;
        }

        if (request.ContainerId is not null || request.CandidateId == Guid.Empty
            || request.Command is null || request.Storage is null)
            return "Create requires a candidate ID, command, and storage semantics, without a container ID.";
        if (!IsRepoDigest(request.RequestedImage))
            return "Create requires an immutable sha256 repository digest.";

        var errors = ApplicationContractValidator.Validate(new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = request.RequestedImage, Command = request.Command, Port = request.ContainerPort
            },
            Storage = request.Storage
        });
        if (errors.Count != 0)
            return string.Join("; ", errors);
        if (request.Environment.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
            || pair.Key.Contains('=') || pair.Key.Contains('\0')
            || pair.Value is null || pair.Value.Contains('\0')))
            return "The environment contains an invalid name or value.";
        return null;
    }
}
