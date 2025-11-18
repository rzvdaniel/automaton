using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Extensions;
using Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;
using Automaton.Studio.Services;
using System.Reflection;

namespace Automaton.Studio.Factories;

public class StepFactory
{
    private const string StepsAssembly = "Automaton.Studio";

    private readonly IServiceProvider serviceProvider;
    private readonly CustomStepsService customStepsService;
    private readonly StepTypeDescriptor stepTypeDescriptor;
    private readonly IDictionary<string, StepExplorerModel> explorerSteps = new Dictionary<string, StepExplorerModel>();
    private readonly IDictionary<string, StepExplorerModel> coreSteps = new Dictionary<string, StepExplorerModel>();
    private readonly IDictionary<string, StepExplorerModel> customSteps = new Dictionary<string, StepExplorerModel>();
    private readonly IDictionary<string, Type> solutionTypes = new Dictionary<string, Type>();

    public StepFactory(StepTypeDescriptor stepTypeDescriptor, IServiceProvider serviceProvider, CustomStepsService customStepsService)
    {
        this.serviceProvider = serviceProvider;
        this.stepTypeDescriptor = stepTypeDescriptor;
        this.customStepsService = customStepsService;

        LoadSteps();
    }

    public IEnumerable<StepExplorerModel> GetSteps()
    {
        // Move "My Steps" (or "Custom Steps") to the top if present
        const string myStepsCategory = "My Steps";
        var allCategories = explorerSteps.Values.ToList();
        var mySteps = allCategories.FirstOrDefault(x => x.Name == myStepsCategory);
        if (mySteps != null)
        {
            allCategories.Remove(mySteps);
            allCategories.Insert(0, mySteps);
        }
        return allCategories;
    }

    public IEnumerable<StepExplorerModel> GetCoreSteps()
    {
        return coreSteps.Values;
    }

    public IEnumerable<StepExplorerModel> GetCustomSteps()
    {
        return customSteps.Values;
    }

    public StudioStep CreateStep(string name, StudioDefinition activeDefinition)
    {
        var descriptor = stepTypeDescriptor.Describe(solutionTypes[name]);
        var step = serviceProvider.GetService(solutionTypes[name]) as StudioStep;
        step.Id = Guid.NewGuid().ToString();
        step.Name = descriptor.Name;
        step.DisplayName = descriptor.DisplayName;
        step.Description = descriptor.Description;
        step.MoreInfo = descriptor.MoreInfo;
        step.Type = descriptor.Type;
        step.Icon = descriptor.Icon;
        step.Definition = activeDefinition;

        step.Created();

        return step;
    }

    public StudioStep CreateCustomStep(CustomStepExplorerModel explorerStep, StudioDefinition activeDefinition)
    {
        var step = new Steps.Custom.CustomStep
        {
            Id = Guid.NewGuid().ToString(),
            Code = explorerStep.Definition.Code,
            Language = explorerStep.Definition.Language,
            CodeInputVariables = explorerStep.Definition.CodeInputVariables,
            CodeOutputVariables = explorerStep.Definition.CodeOutputVariables,
            Name = explorerStep.Name,
            Type = explorerStep.Type,
            DisplayName = explorerStep.DisplayName,
            Description = explorerStep.Description,
            Icon = explorerStep.Icon,
            MoreInfo = explorerStep.MoreInfo,
            Definition = activeDefinition,
            SourceCustomStepId = explorerStep.Id,
            SourceVersion = explorerStep.Definition.Version,
            StepVersion = 1
        };

        step.Created();

        return step;
    }

    public void RemoveStepFromExplorer(Guid id)
    {
        foreach (var category in explorerSteps.Values)
        {
            // Find the step with the matching id in the category's Steps collection
            var stepToRemove = category.Steps.FirstOrDefault(step => step.Id == id);

            if (stepToRemove != null)
            {
                // Remove the step from the category's Steps collection
                category.Steps.Remove(stepToRemove);
                break; // Exit the loop once the step is found and removed
            }
        }
    }

    public StudioStep CreateStep(Step step, StudioDefinition activeDefinition)
    {
        var studioStep = serviceProvider.GetService(solutionTypes[step.Type]) as StudioStep;

        studioStep.Id = step.Id;
        studioStep.Name = step.Name;
        studioStep.Type = step.Type;
        studioStep.DisplayName = step.DisplayName;
        studioStep.Description = step.Description;
        studioStep.ParentId = step.ParentId;
        studioStep.MoreInfo = step.MoreInfo;
        studioStep.Type = step.Type;
        studioStep.Icon = step.Icon;
        studioStep.NextStepId = step.NextStepId;
        studioStep.Inputs = step.Inputs;
        studioStep.Outputs = step.Outputs;
        studioStep.Definition = activeDefinition;

        // Special handling for custom steps to restore version tracking
        if (studioStep is Steps.Custom.CustomStep customStep && step.StepDefinition != null)
        {
            try
            {
                // Deserialize the step definition from the stored data
                var jsonString = step.StepDefinition.ToString();
                var customStepDefinition = System.Text.Json.JsonSerializer.Deserialize<CustomStepDefinition>(jsonString);
                
                if (customStepDefinition != null)
                {
                    // Restore version tracking properties
                    customStep.SourceCustomStepId = customStepDefinition.SourceCustomStepId;
                    customStep.SourceVersion = customStepDefinition.SourceVersion;
                    customStep.StepVersion = customStepDefinition.Version;
                }
            }
            catch
            {
                // If deserialization fails, continue with default values
                // This ensures backward compatibility with existing flows
            }
        }

        // Note: Do not call Created() because this method is used
        // when loading a flow and we do no need all it's logic

        return studioStep;
    }

    private void LoadSteps()
    {        
        LoadAssemblySteps();

        LoadCustomSteps();
    }

    private void LoadAssemblySteps()
    {
        var assemblies = new[] { Assembly.Load(StepsAssembly) };

        var stepTypes = assemblies
            .SelectMany(x => x.GetAllWithBaseClass<StudioStep>())
            .Where(x => !x.IsAbstract && x != typeof(CustomStep));

        foreach (var stepType in stepTypes)
        {
            AddStep(stepType);
        }
    }

    private void LoadCustomSteps()
    {
        var customStepsList = Task.Run(() => customStepsService.List(null)).Result;

        foreach (var customStep in customStepsList)
        {
            AddExplorerCategory(customStep.Category, isCustom: true);

            var explorerStep = CreateCustomStepExplorerModel(customStep);

            // Add to custom steps collection
            var category = customSteps[customStep.Category];
            category.Steps.Add(explorerStep);
            
            // Also add to main collection for backwards compatibility
            var mainCategory = explorerSteps[customStep.Category];
            mainCategory.Steps.Add(explorerStep);
        }
    }

    private void AddExplorerCategory(string categoryName, bool isCustom = false)
    {
        var targetCollection = isCustom ? customSteps : coreSteps;
        
        if (!targetCollection.ContainsKey(categoryName))
        {
            var categoryModel = CreateCategoryModel(categoryName);
            targetCollection.Add(categoryName, categoryModel);
        }
        
        // Also add to the main collection for backwards compatibility
        if (!explorerSteps.ContainsKey(categoryName))
        {
            var categoryModel = CreateCategoryModel(categoryName);
            explorerSteps.Add(categoryName, categoryModel);
        }
    }

    private void AddStep(Type stepType)
    {
        var stepDescriptor = stepTypeDescriptor.Describe(stepType);

        var explorerStep = CreateStepExplorerModel(stepDescriptor);

        solutionTypes.Add(explorerStep.Name, stepType);

        if (stepDescriptor.VisibleInExplorer)
        {
            if (string.IsNullOrEmpty(stepDescriptor.Category))
            {
                coreSteps.Add(explorerStep.Name, explorerStep);
                explorerSteps.Add(explorerStep.Name, explorerStep);
            }
            else
            {
                AddExplorerCategory(stepDescriptor.Category, isCustom: false);

                var coreCategory = coreSteps[stepDescriptor.Category];
                coreCategory.Steps.Add(explorerStep);
                
                var mainCategory = explorerSteps[stepDescriptor.Category];
                mainCategory.Steps.Add(explorerStep);
            }
        }
    }

    public void AddCustomStep(CustomStep customStep)
    {
        AddExplorerCategory(customStep.Category, isCustom: true);

        var explorerStep = CreateCustomStepExplorerModel(customStep);

        var category = customSteps[customStep.Category];
        category.Steps.Add(explorerStep);
        
        // Also add to main collection for backwards compatibility
        var mainCategory = explorerSteps[customStep.Category];
        mainCategory.Steps.Add(explorerStep);
    }

    public void UpdateCustomStep(CustomStep customStep)
    {
        // Update in custom steps collection
        var explorerCategory = customSteps[customStep.Category];
        var steps = explorerCategory.Steps;
        var index = steps.FindIndex(x => x.Id == customStep.Id);
        if (index >= 0)
        {
            steps[index] = CreateCustomStepExplorerModel(customStep);
        }
        
        // Also update in main collection for backwards compatibility
        var mainCategory = explorerSteps[customStep.Category];
        var mainSteps = mainCategory.Steps;
        var mainIndex = mainSteps.FindIndex(x => x.Id == customStep.Id);
        if (mainIndex >= 0)
        {
            mainSteps[mainIndex] = CreateCustomStepExplorerModel(customStep);
        }
    }

    private static StepExplorerModel CreateStepExplorerModel(StepDescriptor stepDescriptor)
    {
        var stepExplorerModel = new StepExplorerModel
        {
            Name = stepDescriptor.Name,
            Type = stepDescriptor.Type,
            Description = stepDescriptor.Description,
            DisplayName = stepDescriptor.DisplayName,
            MoreInfo = stepDescriptor.MoreInfo,
            Category = stepDescriptor.Category,
            VisibleInExplorer = stepDescriptor.VisibleInExplorer,
            Icon = stepDescriptor.Icon
        };

        return stepExplorerModel;
    }

    private static CustomStepExplorerModel CreateCustomStepExplorerModel(CustomStep customStep)
    {
        var stepExplorerModel = new CustomStepExplorerModel
        {
            Id = customStep.Id,
            Name = customStep.Name,
            Type = customStep.Type,
            Description = customStep.Description,
            DisplayName = customStep.DisplayName,
            MoreInfo = customStep.MoreInfo,
            Category = customStep.Category,
            VisibleInExplorer = customStep.VisibleInExplorer,
            Icon = customStep.Icon,
            Definition = customStep.Definition
        };

        return stepExplorerModel;
    }

    private static StepExplorerModel CreateCategoryModel(string category)
    {
        var stepExplorerModel = new StepExplorerModel
        {
            Name = category,
            Type = string.Empty,
            Description = category,
            DisplayName = category,
            Category = string.Empty,
            MoreInfo = string.Empty,
            VisibleInExplorer = true,
            Icon = string.Empty
        };

        return stepExplorerModel;
    }
}
