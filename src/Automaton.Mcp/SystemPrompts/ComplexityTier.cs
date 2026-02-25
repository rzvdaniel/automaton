namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Complexity tier used to select the appropriate system prompt template for AI app generation.
/// </summary>
public enum ComplexityTier
{
    /// <summary>Landing pages, simple forms, static content, trivial prototypes.</summary>
    Simple,

    /// <summary>Dashboards with a few API calls, simple CRUD operations.</summary>
    Medium,

    /// <summary>Multi-feature apps (e-commerce, booking), feature-based folder layout, multiple controllers.</summary>
    Large,

    /// <summary>CRM/ERP-grade apps, many modules, strict tooling policies, shared utilities.</summary>
    Enterprise
}
