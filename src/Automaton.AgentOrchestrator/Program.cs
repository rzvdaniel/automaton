using Automaton.AgentOrchestrator.Hosting;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

if (args is ["--build-identity"])
{
    var identity = new AssemblyBuildIdentity().Current;
    Console.WriteLine($"{identity.Version}\t{identity.Revision}");
    return;
}

if (args is ["--retry-blocked", var workItemId])
{
    var commandBuilder = Host.CreateApplicationBuilder(args);
    var commandWorkflowPath = commandBuilder.Configuration["AgentOrchestrator:WorkflowPath"] ?? Path.Combine(Environment.CurrentDirectory, "WORKFLOW.md");
    var commandWorkflow = await new WorkflowLoader().LoadAsync(commandWorkflowPath, CancellationToken.None);
    commandBuilder.Services.AddSingleton(commandWorkflow);
    commandBuilder.Services.AddSingleton(commandWorkflow.Options);
    commandBuilder.Services.AddAgentOrchestrator();
    await using var commandProvider = commandBuilder.Services.BuildServiceProvider();
    var result = await commandProvider.GetRequiredService<Automaton.AgentOrchestrator.Orchestration.IBlockedRunRecoveryOperator>().RequestRetryAsync(workItemId, CancellationToken.None);
    Console.WriteLine($"{result.Category}: {result.Message}");
    Environment.ExitCode = result.Accepted ? 0 : 2;
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
