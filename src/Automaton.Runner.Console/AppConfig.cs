namespace Automaton.Runner.Console;

public class AppConfig
{
    public string ApplicationType { get; set; } = "ConsoleRunner";
    public string BaseUrl { get; set; } = string.Empty;
    public string WorkflowHubUrl { get; set; } = "api/workflow/hub";
    public string FlowsUrl { get; set; } = "api/flows";
    public string RunnersUrl { get; set; } = "api/runners";
    public string LogsUrl { get; set; } = "api/logs";
    public string FlowExecutionUrl { get; set; } = "api/flowexecutions";
}