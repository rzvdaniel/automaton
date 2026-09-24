using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Api.Models;

namespace Automaton.Api.Services;

internal static class HostContainerRelayRequestValidator
{
    public static HostContainerExecutionResponse? ValidateCaller(RunnerDetails caller)
    {
        if (caller.Purpose != RunnerPurposes.Managed || caller.HostedRunnerId is null)
        {
            return Failure("caller_not_managed_runner", "The caller is not a Managed Hosted Runner.");
        }

        return null;
    }

    public static HostContainerExecutionResponse? ValidateOperation(HostContainerExecutionRequest request)
    {
        var operationFailure = ValidateOperationShape(request);
        if (operationFailure is not null)
        {
            return operationFailure;
        }

        if (HostContainerRuntimeValidation.IsRuntimeOperation(request.Operation))
        {
            return ValidateRuntimeRequest(request);
        }

        return null;
    }

    private static HostContainerExecutionResponse? ValidateOperationShape(HostContainerExecutionRequest request)
    {
        var runtimeOperation = HostContainerRuntimeValidation.IsRuntimeOperation(request.Operation);
        if (!runtimeOperation && request.Operation is not (HostContainerOperationKinds.ResolveImage or HostContainerOperationKinds.RunBuild))
        {
            return Failure("unsupported_operation", "The host-container operation is not supported.");
        }

        if (!runtimeOperation && string.IsNullOrWhiteSpace(request.RequestedImage))
        {
            return Failure("invalid_request", "ResolveImage requires an image reference.");
        }

        if (request.Operation == HostContainerOperationKinds.ResolveImage)
        {
            return ValidateResolveImage(request);
        }

        return request.Operation == HostContainerOperationKinds.RunBuild
            ? ValidateBuildRequest(request)
            : null;
    }

    private static HostContainerExecutionResponse? ValidateResolveImage(HostContainerExecutionRequest request)
    {
        if (request.Resources is not null || request.ContainerId is not null || request.Command is not null
            || request.CandidateId != Guid.Empty || request.Storage is not null
            || request.HostPort != 0 || request.ContainerPort != 0 || request.Environment is null || request.Environment.Count != 0)
        {
            return Failure("unsupported_operation", "ResolveImage does not accept container execution inputs.");
        }

        return null;
    }

    private static HostContainerExecutionResponse? ValidateBuildRequest(HostContainerExecutionRequest request)
    {
        var inputFailure = ValidateBuildInputs(request);
        if (inputFailure is not null)
        {
            return inputFailure;
        }

        var command = request.Command!;
        var contractErrors = ApplicationContractValidator.Validate(new ApplicationContract
        {
            Build = new ApplicationBuildSpec
            {
                Image = request.RequestedImage,
                Command = command,
                TimeoutSeconds = request.TimeoutSeconds
            }
        });
        if (contractErrors.Count > 0)
        {
            return Failure("invalid_request", string.Join("; ", contractErrors));
        }

        var image = request.RequestedImage!;
        if (image.Any(char.IsWhiteSpace) || image.Contains('\0') || image.StartsWith("-", StringComparison.Ordinal))
        {
            return Failure("invalid_request", "RunBuild contains an invalid image reference.");
        }

        return request.Scope.DeploymentAttemptId == Guid.Empty
            ? Failure("invalid_request", "RunBuild requires a server-issued deployment attempt.")
            : null;
    }

    private static HostContainerExecutionResponse? ValidateBuildInputs(HostContainerExecutionRequest request)
    {
        if (request.Command is null || request.Resources is not null || request.ContainerId is not null
            || request.CandidateId != Guid.Empty || request.Storage is not null
            || request.HostPort != 0 || request.ContainerPort != 0)
        {
            return Failure("invalid_request", "RunBuild requires a command and server-derived resources.");
        }

        if (request.TimeoutSeconds is < 1 or > 86_400)
        {
            return Failure("invalid_request", "The build timeout must be between 1 and 86400 seconds.");
        }

        var command = request.Command!;
        if (request.Environment is null || command.Arguments is null || command.Environment is null
            || string.IsNullOrWhiteSpace(command.Executable))
        {
            return Failure("invalid_request", "RunBuild contains an invalid command or environment.");
        }

        return null;
    }

    private static HostContainerExecutionResponse? ValidateRuntimeRequest(HostContainerExecutionRequest request)
    {
        if (request.Resources is not null)
        {
            return Failure("invalid_request", "Container resources are derived by the server.");
        }

        var runtimeError = HostContainerRuntimeValidation.Validate(request);
        return runtimeError is null ? null : Failure("invalid_request", runtimeError);
    }

    private static HostContainerExecutionResponse Failure(string code, string message) =>
        new() { ErrorCode = code, Error = message };
}
