namespace Automaton.Api.Models;

public sealed record SecretOwner(string OwnerType, Guid OwnerId);

public sealed record SecretGrantScope(
    Guid OperationId,
    Guid AppId,
    string ActionKey,
    Guid RunnerId,
    Guid? RunnerHostId,
    string OwnerType,
    Guid OwnerId);

public sealed record SecretVersionCreatedResult(
    Guid SecretDefinitionId,
    Guid SecretVersionId,
    int Version);

public sealed record ExecutionSecretLease(
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyCollection<Guid> GrantIds);
