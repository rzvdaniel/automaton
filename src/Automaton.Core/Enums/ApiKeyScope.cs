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
    /// Can register and operate runners (includes Execute + ReadOnly permissions)
    /// </summary>
    Runner = 3,
    
    /// <summary>
    /// All permissions - recommended for console runners and automation
    /// </summary>
    FullAccess = 4,
    
  /// <summary>
    /// Can execute flows on behalf of published web apps (web app execution only)
    /// ?? Always add new values at the end to preserve serialized data compatibility
    /// </summary>
    WebApp = 5
}
