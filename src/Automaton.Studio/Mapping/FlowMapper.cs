using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Factories;

namespace Automaton.Studio.Mapping;

public static class FlowMapper
{
    public static StudioFlow ToStudioFlow(Flow flow, StepFactory stepFactory)
    {
        var studioFlow = new StudioFlow
        {
            Id = flow.Id,
            Name = flow.Name,
            Created = flow.Created,
            Updated = flow.Updated,
            StartupDefinitionId = flow.StartupDefinitionId,
            InputVariables = flow.InputVariables,
            OutputVariables = flow.OutputVariables,
            Variables = flow.Variables,
            Definitions = new List<StudioDefinition>()
        };

        if (flow.Definitions != null)
        {
            foreach (var definition in flow.Definitions)
            {
                var studioDefinition = new StudioDefinition
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Flow = studioFlow,
                    Steps = new List<StudioStep>()
                };

                if (definition.Steps != null)
                {
                    foreach (var step in definition.Steps)
                    {
                        var studioStep = stepFactory.CreateStep(step, studioDefinition);
                        studioDefinition.Steps.Add(studioStep);
                    }
                }

                studioFlow.Definitions.Add(studioDefinition);
            }
        }

        return studioFlow;
    }

    public static Flow ToFlow(StudioFlow studioFlow)
    {
        var flow = new Flow
        {
            Id = studioFlow.Id,
            Name = studioFlow.Name,
            StartupDefinitionId = studioFlow.StartupDefinitionId,
            Created = studioFlow.Created,
            Updated = studioFlow.Updated,
            Variables = studioFlow.Variables != null ? new Dictionary<string, StepVariable>(studioFlow.Variables) : new Dictionary<string, StepVariable>(),
            InputVariables = studioFlow.InputVariables != null ? new Dictionary<string, StepVariable>(studioFlow.InputVariables) : new Dictionary<string, StepVariable>(),
            OutputVariables = studioFlow.OutputVariables != null ? new Dictionary<string, StepVariable>(studioFlow.OutputVariables) : new Dictionary<string, StepVariable>(),
            Definitions = studioFlow.Definitions?.Select(def => new Definition
            {
                Id = def.Id,
                Name = def.Name,
                Steps = def.Steps?.Select(step => new Step
                {
                    Id = step.Id,
                    Name = step.Name,
                    DisplayName = step.DisplayName,
                    Description = step.Description,
                    Type = step.Type,
                    MoreInfo = step.MoreInfo,
                    Icon = step.Icon,
                    Inputs = step.Inputs != null ? new Dictionary<string, StepVariable>(step.Inputs) : new Dictionary<string, StepVariable>(),
                    Outputs = step.Outputs != null ? new Dictionary<string, StepVariable>(step.Outputs) : new Dictionary<string, StepVariable>(),
                    NextStepId = step.NextStepId,
                    ParentId = step.ParentId
                }).ToList() ?? new List<Step>()
            }).ToList() ?? new List<Definition>()
        };

        return flow;
    }
}
