namespace Automaton.Core.Enums;

/// <summary>
/// Defines the access scope for API keys
/// </summary>
public enum ApiKeyScope
{
    /// <summary>
    /// Can read flows and executions only
    /// </summary>
    ReadOnly = 1,
    
    /// <summary>
    /// Can execute workflows (includes ReadOnly permissions)
    /// </summary>
    Execute = 2,
    
    /// <summary>
    /// Runner credential used by registered runner processes.
    /// </summary>
    Runner = 3,
    
    /// <summary>
    /// Broad legacy access for advanced integrations.
    /// </summary>
    FullAccess = 4,
    
  /// <summary>
    /// Can execute flows on behalf of published web apps (web app execution only)
    /// ?? Always add new values at the end to preserve serialized data compatibility
    /// </summary>
    WebApp = 5,

    /// <summary>
    /// Single-use, short-lived credential for Infrastructure Runner host registration.
    /// </summary>
    InfrastructureRunnerEnroll = 6,

    /// <summary>
    /// Developer credential for the Automaton CLI and normal app lifecycle operations.
    /// It does not grant API-key management, administration, billing, or infrastructure enrollment access.
    /// </summary>
    Developer = 7
}
