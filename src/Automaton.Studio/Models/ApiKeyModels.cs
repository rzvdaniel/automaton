#nullable enable

namespace Automaton.Studio.Models;

public class ApiKeyResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string KeyPrefix { get; set; }
    public ApiKeyScope Scope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public class CreateApiKeyRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.Runner;
}

public class CreateApiKeyResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ApiKey { get; set; } // Only returned during creation
    public required string KeyPrefix { get; set; }
    public ApiKeyScope Scope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public enum ApiKeyScope
{
    Runner = 1,      // Can register and operate runners
    ReadOnly = 2,    // Can read flows and executions
    Execute = 3      // Can execute workflows
}