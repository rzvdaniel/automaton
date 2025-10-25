#nullable enable

using Automaton.Core.Enums;

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
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.Runner; // Default to Runner for consistency
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