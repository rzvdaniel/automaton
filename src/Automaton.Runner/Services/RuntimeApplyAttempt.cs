using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed record RuntimeApplyAttempt(
    Guid AppId,
    Guid TargetId,
    ApplicationContract Contract,
    string MetadataPath,
    string Artifact,
    RunnerApplicationStorageRuntime Storage,
    string RuntimeImage,
    int InitialPort,
    Guid CandidateId,
    IScopedApplicationContainerRuntime? ScopedRuntime,
    RunnerRuntimeDeploymentMetadata Candidate,
    RunnerRuntimeDeploymentMetadata Pending,
    RunnerRuntimeDeploymentMetadata? Previous)
{
    public int EffectivePort { get; set; } = InitialPort;
}
