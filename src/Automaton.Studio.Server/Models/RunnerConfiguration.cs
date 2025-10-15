namespace Automaton.Studio.Server.Models;

/// <summary>
/// Helper class for deserializing the Runner.Configuration JSON property
/// </summary>
public class RunnerConfiguration
{
    public RunnerPythonConfig? Python { get; set; }
    
    // Future configuration sections can be added here
    // public NodeJsConfig? NodeJs { get; set; }
    // public JavaConfig? Java { get; set; }
}
