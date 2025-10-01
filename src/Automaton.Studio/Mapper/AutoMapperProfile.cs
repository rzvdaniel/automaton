using AutoMapper;
using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.CustomSteps;
using Automaton.Studio.Pages.Flows;
using Automaton.Studio.Steps.ExecuteFlow;

namespace Automaton.Studio.Mapper;

public class AutoMapperProfile : Profile
{
    private readonly IServiceProvider serviceProvider;

    public AutoMapperProfile(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;

        CreateMap<StudioFlow, Flow>();
        
        // Enhanced mapping for StudioStep to preserve custom step definitions
        CreateMap<StudioStep, Step>()
            .ForMember(dest => dest.StepDefinition, opt => opt.MapFrom(src => GetCustomStepDefinition(src)));
        
        CreateMap<StudioDefinition, Definition>();

        CreateMap<FlowInfo, ExecuteFlowModel>();
        CreateMap<FlowInfo, FlowModel>();
        CreateMap<WorkflowExecution, FlowExecution>();
        CreateMap<CustomStep, CustomStepListItem>();   
    }

    private static object? GetCustomStepDefinition(StudioStep studioStep)
    {
        if (studioStep is not Steps.Custom.CustomStep customStep)
        {
            return null;
        }

        return new
        {
            Code = customStep.Code,
            Language = customStep.Language,
            CodeInputVariables = customStep.CodeInputVariables,
            CodeOutputVariables = customStep.CodeOutputVariables,
            SourceCustomStepId = customStep.SourceCustomStepId,
            SourceVersion = customStep.SourceVersion,
            Version = customStep.StepVersion
        };
    }
}
