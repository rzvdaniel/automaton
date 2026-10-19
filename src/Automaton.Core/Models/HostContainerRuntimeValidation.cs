using System.Text.RegularExpressions;

namespace Automaton.Core.Models;

public static class HostContainerRuntimeValidation
{
    public static bool IsRuntimeOperation(string operation) => operation is
        HostContainerOperationKinds.CreateApplicationContainer
        or HostContainerOperationKinds.ApplyComposeProject or HostContainerOperationKinds.InspectComposeProject
        or HostContainerOperationKinds.RemoveComposeProject
        or HostContainerOperationKinds.StartContainer
        or HostContainerOperationKinds.InspectContainer or HostContainerOperationKinds.GetContainerLogs
        or HostContainerOperationKinds.RemoveContainer or HostContainerOperationKinds.RemoveTargetContainers;

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

        return request.Operation switch
        {
            HostContainerOperationKinds.CreateApplicationContainer => ValidateCreateRequest(request),
            HostContainerOperationKinds.ApplyComposeProject => ValidateComposeRequest(request),
            HostContainerOperationKinds.InspectComposeProject => ValidateInspectComposeRequest(request),
            HostContainerOperationKinds.RemoveComposeProject => ValidateRemoveComposeRequest(request),
            _ => ValidateExistingContainerRequest(request)
        };
    }

    private static string? ValidateComposeRequest(HostContainerExecutionRequest request)
    {
        if (request.CandidateId == Guid.Empty || request.Compose is null || request.ContainerId is not null
            || request.RequestedImage is not null || request.Command is not null || request.Storage is not null
            || request.User is not null || request.ContainerPort != 0 || request.Environment!.Count != 0)
            return "ApplyComposeProject requires a normalized Compose model and candidate identity only.";
        var errors = ApplicationContractValidator.ValidateForDeployment(new ApplicationContract { Compose = request.Compose });
        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private static string? ValidateRemoveComposeRequest(HostContainerExecutionRequest request)
    {
        if (request.CandidateId == Guid.Empty || request.Compose is not null || request.ContainerId is not null
            || request.RequestedImage is not null || request.Command is not null || request.Storage is not null
            || request.User is not null || request.ContainerPort != 0 || request.Environment!.Count != 0)
            return "RemoveComposeProject requires only the candidate identity and scope.";
        return null;
    }

    private static string? ValidateInspectComposeRequest(HostContainerExecutionRequest request)
    {
        if (request.CandidateId == Guid.Empty || request.Compose is null || request.ContainerId is not null
            || request.RequestedImage is not null || request.Command is not null || request.Storage is not null
            || request.User is not null || request.ContainerPort != 0 || request.Environment!.Count != 0)
            return "InspectComposeProject requires a normalized Compose model and candidate identity only.";
        var errors = ApplicationContractValidator.ValidateForDeployment(new ApplicationContract { Compose = request.Compose });
        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private static string? ValidateExistingContainerRequest(HostContainerExecutionRequest request)
    {
        if (request.Operation == HostContainerOperationKinds.RemoveTargetContainers
            ? request.ContainerId is not null : !IsContainerReference(request.ContainerId))
            return "A valid container ID or name is required.";
        if (request.RequestedImage is not null || request.Command is not null || request.Storage is not null
            || request.Compose is not null || request.User is not null
            || request.Resources is not null || request.ContainerLimits is not null || request.CandidateId != Guid.Empty
            || request.ContainerPort != 0 || request.Environment!.Count != 0)
            return "Existing-container operations accept only scope and container identity.";
        return null;
    }

    private static string? ValidateCreateRequest(HostContainerExecutionRequest request)
    {
        if (request.ContainerId is not null || request.CandidateId == Guid.Empty
            || request.Command is null || request.Storage is null || request.Compose is not null)
            return "Create requires a candidate ID, command, and storage semantics, without a container ID.";
        var errors = ApplicationContractValidator.Validate(new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = request.RequestedImage, Command = request.Command, Port = request.ContainerPort,
                User = request.User
            },
            Storage = request.Storage
        });
        if (errors.Count != 0)
            return string.Join("; ", errors);
        if (HasInvalidEnvironmentEntry(request.Environment!))
            return "The environment contains an invalid name or value.";
        return null;
    }

    private static bool HasInvalidEnvironmentEntry(IReadOnlyDictionary<string, string> environment) =>
        environment.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
            || pair.Key.Contains('=') || pair.Key.Contains('\0')
            || pair.Value is null || pair.Value.Contains('\0'));
}
