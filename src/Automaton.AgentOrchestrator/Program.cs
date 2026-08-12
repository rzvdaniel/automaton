using Automaton.AgentOrchestrator.Hosting;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Hosting;

if (args is ["--build-identity"])
{
    var identity = new AssemblyBuildIdentity().Current;
    Console.WriteLine($"{identity.Version}\t{identity.Revision}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
var workflowPath = builder.Configuration["AgentOrchestrator:WorkflowPath"]
    ?? Path.Combine(Environment.CurrentDirectory, "WORKFLOW.md");
var workflow = await new WorkflowLoader().LoadAsync(workflowPath, CancellationToken.None);
if (builder.Configuration.GetValue("AgentOrchestrator:ProductionMode", false))
    LinuxProductionStartupValidator.Validate(workflow, AppContext.BaseDirectory);
builder.Services.AddSingleton(workflow);
builder.Services.AddSingleton(workflow.Options);
builder.Services.AddAgentOrchestrator();

await builder.Build().RunAsync();
