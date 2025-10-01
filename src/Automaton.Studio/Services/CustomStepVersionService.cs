using Automaton.Studio.Domain;

namespace Automaton.Studio.Services;

public class CustomStepVersionService
{
    private readonly CustomStepsService customStepsService;

    public CustomStepVersionService(CustomStepsService customStepsService)
    {
        this.customStepsService = customStepsService;
    }

    /// <summary>
    /// Checks if a custom step instance in a flow is outdated compared to its template.
    /// </summary>
    /// <param name="stepInstance">The custom step instance from a flow</param>
    /// <returns>True if an update is available, false otherwise</returns>
    public async Task<bool> IsUpdateAvailableAsync(Steps.Custom.CustomStep stepInstance)
    {
        // Get version tracking from the step's properties
        var sourceId = stepInstance.SourceCustomStepId;
        var sourceVersion = stepInstance.SourceVersion;

        if (sourceId == null || sourceVersion == null || sourceVersion <= 0)
        {
            return false;
        }

        try
        {
            // Get the current version of the template
            var template = await customStepsService.Load(sourceId.Value);
            
            if (template == null)
            {
                // Template was deleted, no update available
                return false;
            }

            // Compare versions
            return template.Definition.Version > sourceVersion;
        }
        catch
        {
            // If we can't check, assume no update available
            return false;
        }
    }

    /// <summary>
    /// Gets the latest version of a custom step template.
    /// </summary>
    /// <param name="sourceCustomStepId">ID of the template</param>
    /// <returns>The latest template version, or null if not found</returns>
    public async Task<CustomStep?> GetLatestTemplateAsync(Guid sourceCustomStepId)
    {
        try
        {
            return await customStepsService.Load(sourceCustomStepId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Updates a step instance with the latest version from its template.
    /// </summary>
    /// <param name="stepInstance">The step instance to update</param>
    /// <returns>True if update was successful, false otherwise</returns>
    public async Task<bool> UpdateToLatestVersionAsync(Steps.Custom.CustomStep stepInstance)
    {
        var sourceId = stepInstance.SourceCustomStepId;
        
        if (sourceId == null)
        {
            return false;
        }

        try
        {
            var latestTemplate = await GetLatestTemplateAsync(sourceId.Value);
            
            if (latestTemplate == null)
            {
                return false;
            }

            // Preserve user's input values but update everything else
            var existingInputValues = stepInstance.CodeInputVariables
                .ToDictionary(v => v.Name, v => v.Value);

            // Update the step with latest template data
            stepInstance.Code = latestTemplate.Definition.Code;
            stepInstance.Language = latestTemplate.Definition.Language;
            stepInstance.CodeInputVariables = latestTemplate.Definition.CodeInputVariables
                .Select(templateVar => new Core.Models.StepVariable
                {
                    Id = templateVar.Id,
                    Name = templateVar.Name,
                    Type = templateVar.Type,
                    Description = templateVar.Description,
                    // Preserve existing value if variable name matches
                    Value = existingInputValues.TryGetValue(templateVar.Name, out var existingValue) 
                        ? existingValue 
                        : templateVar.Value
                }).ToList();
            
            stepInstance.CodeOutputVariables = latestTemplate.Definition.CodeOutputVariables;
            
            // Update tracking info
            stepInstance.SourceVersion = latestTemplate.Definition.Version;
            stepInstance.StepVersion = latestTemplate.Definition.Version;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets version comparison info for display purposes.
    /// </summary>
    /// <param name="stepInstance">The step instance to check</param>
    /// <returns>Version comparison info</returns>
    public async Task<CustomStepVersionInfo> GetVersionInfoAsync(Steps.Custom.CustomStep stepInstance)
    {
        var currentVersion = stepInstance.SourceVersion ?? 1;
        var sourceId = stepInstance.SourceCustomStepId;

        var info = new CustomStepVersionInfo
        {
            CurrentVersion = currentVersion,
            IsUpdateAvailable = false,
            LatestVersion = currentVersion
        };

        if (sourceId != null)
        {
            var latestTemplate = await GetLatestTemplateAsync(sourceId.Value);
            if (latestTemplate != null)
            {
                info.LatestVersion = latestTemplate.Definition.Version;
                info.IsUpdateAvailable = latestTemplate.Definition.Version > currentVersion;
            }
        }

        return info;
    }
}

public class CustomStepVersionInfo
{
    public int CurrentVersion { get; set; }
    public int LatestVersion { get; set; }
    public bool IsUpdateAvailable { get; set; }
}