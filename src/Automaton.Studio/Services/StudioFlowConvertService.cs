using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Studio.Services;

public class StudioFlowConvertService : IFlowConvertService<StudioFlow>
{
    private readonly IServiceProvider serviceProvider;

    public StudioFlowConvertService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public Workflow ConvertFlow(StudioFlow flow)
    {
        var workflow = new Workflow
        {
            Id = flow.Id,
            Name = flow.Name,
            Variables = flow.Variables,
            OutputVariables = flow.OutputVariables,
            StartupDefinitionId = flow.StartupDefinitionId
        };

        foreach (var definition in flow.Definitions)
        {
            var workflowDefinition = new WorkflowDefinition
            {
                Id = definition.Id
            };

            workflowDefinition.Steps = ConvertSteps(definition.Steps, workflowDefinition);

            workflow.Definitions.Add(workflowDefinition);
        }

        return workflow;
    }

    private IDictionary<string, WorkflowStep> ConvertSteps(ICollection<StudioStep> steps, WorkflowDefinition workflowDefinition)
    {
        var workflowSteps = new Dictionary<string, WorkflowStep>();

        // Create a scope to resolve scoped services (like CustomStep which requires ScriptEngineHost)
        using (var scope = serviceProvider.CreateScope())
        {
            var scopedProvider = scope.ServiceProvider;

            foreach (var step in steps)
            {
                var stepType = step.FindType();
                var workflowStep = scopedProvider.GetService(stepType) as WorkflowStep;

                if (workflowStep == null)
                {
                    throw new InvalidOperationException($"Could not resolve WorkflowStep for type {stepType?.Name ?? "null"}. Make sure the step is registered in the service container.");
                }

                UpdateStep(ref workflowStep, step, workflowDefinition);
                workflowSteps.Add(step.Id, workflowStep);
            }
        }

        return workflowSteps;
    }

    private static void UpdateStep(ref WorkflowStep step, StudioStep studioStep, WorkflowDefinition workflowDefinition)
    {
        step.Id = studioStep.Id;
        step.Name = studioStep.Name;
        step.DisplayName = studioStep.DisplayName;
        step.Description = studioStep.Description;
        step.Type = studioStep.Type;
        step.NextStepId = studioStep.NextStepId;
        step.ParentId = studioStep.ParentId;
        step.Inputs = studioStep.Inputs;
        step.Outputs = studioStep.Outputs;
        step.WorkflowDefinition = workflowDefinition;
    }
}
