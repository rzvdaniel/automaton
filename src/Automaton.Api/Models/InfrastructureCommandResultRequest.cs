using Automaton.Core.Models;

namespace Automaton.Api.Models;

public sealed record InfrastructureCommandResultRequest(Guid OperationId, Guid RunnerHostId,
    Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds,
    AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result);
