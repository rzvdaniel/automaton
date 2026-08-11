using Automaton.AgentOrchestrator.Hosting;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var workflowPath = builder.Configuration["AgentOrchestrator:WorkflowPath"]
    ?? Path.Combine(Environment.CurrentDirectory, "WORKFLOW.md");
var workflow = await new WorkflowLoader().LoadAsync(workflowPath, CancellationToken.None);
builder.Services.AddSingleton(workflow);
builder.Services.AddSingleton(workflow.Options);
builder.Services.AddAgentOrchestrator();

await builder.Build().RunAsync();
