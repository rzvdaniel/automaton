# Technical Approach Document: Runner & Desktop Script Engine Settings

**Document Version**: 1.0  
**Date**: 2024-01-XX  
**Status**: Draft  
**Author**: Development Team

---

## Table of Contents
1. [Overview](#overview)
2. [Problem Statement](#problem-statement)
3. [Architecture Goals](#architecture-goals)
4. [Technical Design](#technical-design)
5. [Implementation Steps](#implementation-steps)
6. [Data Models](#data-models)
7. [API Specifications](#api-specifications)
8. [UI/UX Design](#uiux-design)
9. [Testing Strategy](#testing-strategy)
10. [Risks & Mitigations](#risks--mitigations)
11. [Future Extensibility](#future-extensibility)

---

## Overview

This document outlines the technical approach for implementing a flexible, user-configurable system for Python path settings that works for both:
1. **Runners** (execution environment) - machine-specific settings per runner
2. **Automaton Studio Desktop** (development environment) - local machine settings

The solution uses a nested JSON structure for future extensibility to support additional script engines (Node.js, Bash, PowerShell, etc.).

---

## Problem Statement

### Current Issues
- Auto-detection of Python paths is not always accurate
- No way for users to specify custom Python DLL paths or package paths
- Settings are machine-specific but not configurable
- No differentiation between runner environments and desktop development environment

### Requirements
1. Allow users to configure Python paths via UI (not config files)
2. Support different settings for different runners
3. Support local settings for Automaton Studio Desktop
4. Prioritize user-specified paths over auto-detection
5. Design for future extensibility (other script engines)
6. Persist settings in database

---

## Architecture Goals

### Priority Order
```
User-specified paths ? Auto-detection (fallback)
```

### Separation of Concerns
- **Runner Settings**: Per-runner configuration stored in Runner entity
- **Desktop Settings**: Machine-specific configuration in Settings table

### Extensibility
- Nested JSON structure to support future script engines
- Generic Settings table for Desktop
- ScriptEngineSettings property for Runners

### User Experience
- UI-based configuration (no manual config file editing)
- Visual feedback for configuration
- Easy to understand and maintain

---

## Technical Design

### High-Level Architecture

```
???????????????????????????????????????????????????????????????
?                         User Interface                       ?
?  ??????????????????????      ??????????????????????????    ?
?  ?  Runner Settings   ?      ?   Desktop Settings     ?    ?
?  ?  (per Runner)      ?      ?   (local machine)      ?    ?
?  ??????????????????????      ??????????????????????????    ?
???????????????????????????????????????????????????????????????
               ?                           ?
               ?                           ?
????????????????????????????  ?????????????????????????????
?  Runner.ScriptEngine     ?  ?    Settings Table         ?
?  Settings (JSON)         ?  ?    Key: "PythonSettings"  ?
?                          ?  ?    Value: JSON            ?
????????????????????????????  ?????????????????????????????
               ?                           ?
               ?????????????????????????????
                           ?
               ?????????????????????????????
               ?  ScriptEngineConfiguration ?
               ?  - PythonDllPath           ?
               ?  - PythonPaths             ?
               ?  - AutoDetectPaths         ?
               ?????????????????????????????
                           ?
                           ?
               ?????????????????????????????
               ?   PythonNetScriptEngine    ?
               ?   (uses configuration)     ?
               ?????????????????????????????
```

### Configuration Loading Priority

```csharp
// Priority: User Settings ? Auto-Detection

if (userSettings?.Python?.PythonDllPath != null)
{
    config.PythonDllPath = userSettings.Python.PythonDllPath;
}
else if (config.AutoDetectPythonPaths)
{
    // Auto-detect Python DLL
    config.PythonDllPath = AutoDetectPythonDll();
}

if (userSettings?.Python?.PythonPaths?.Any() == true)
{
    config.PythonPaths = userSettings.Python.PythonPaths;
}
else if (config.AutoDetectPythonPaths)
{
    // Auto-detect Python paths
    config.PythonPaths = AutoDetectPythonPaths();
}
```

---

## Implementation Steps

### Phase 1: Core Models & Database Schema

#### Step 1.1: Create Script Engine Settings Models
**Location**: `src/Automaton.Core/Models/ScriptEngineSettings.cs`

**Create Classes**:
```csharp
/// <summary>
/// Root container for all script engine settings
/// </summary>
public class RunnerScriptEngineSettings 
{
    /// <summary>
    /// Python engine specific settings
    /// </summary>
    public PythonEngineSettings? Python { get; set; }
    
    // Future extensibility:
    // public NodeJsEngineSettings? NodeJs { get; set; }
    // public BashEngineSettings? Bash { get; set; }
}

/// <summary>
/// Python-specific engine configuration
/// </summary>
public class PythonEngineSettings
{
    /// <summary>
    /// Path to Python DLL (e.g., C:\Python312\python312.dll)
    /// </summary>
    public string? PythonDllPath { get; set; }
    
    /// <summary>
    /// List of paths to search for Python packages
    /// </summary>
    public List<string> PythonPaths { get; set; } = new();
    
    // Future Python-specific settings:
    // public bool? EnableVirtualEnv { get; set; }
    // public string? VirtualEnvPath { get; set; }
    // public List<string>? AllowedPackages { get; set; }
}
```

**Dependencies**: None  
**Estimated Effort**: 15 minutes

---

#### Step 1.2: Update Runner Entity
**Location**: `src/Automaton.Studio.Server/Entities/Runner.cs`

**Changes**:
```csharp
public class Runner
{
    [Required]
    public Guid Id { get; set; }
    
    [Required]
    public string Name { get; set; }
    
    public string ConnectionId { get; set; }
    
    /// <summary>
    /// JSON-serialized script engine configuration settings
    /// Stores RunnerScriptEngineSettings as JSON string
    /// </summary>
    public string? ScriptEngineSettings { get; set; }

    public virtual IEnumerable<RunnerUser> RunnerUsers { get; set; }
}
```

**Dependencies**: Step 1.1  
**Estimated Effort**: 5 minutes

---

#### Step 1.3: Create Settings Entity
**Location**: `src/Automaton.Studio.Server/Entities/Setting.cs`

**Create Entity**:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Generic key-value settings storage for application configuration
/// Values are stored as JSON strings for flexibility
/// </summary>
public class Setting
{
    /// <summary>
    /// Unique key identifier for the setting
    /// Example: "PythonSettings", "EmailConfiguration", etc.
    /// </summary>
    [Key]
    [Required]
    [MaxLength(255)]
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// JSON-serialized value of the setting
    /// Can store simple strings or complex objects
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp of last update for audit purposes
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Optional description of what this setting controls
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }
}
```

**Dependencies**: None  
**Estimated Effort**: 10 minutes

---

#### Step 1.4: Create Database Migrations
**Location**: Auto-generated in `src/Automaton.Studio.Server/Migrations/`

**Migration 1: Add ScriptEngineSettings to Runner**
```bash
# Run in Automaton.Studio.Server directory
dotnet ef migrations add AddScriptEngineSettingsToRunner
```

**Migration 2: Create Settings Table**
```bash
dotnet ef migrations add CreateSettingsTable
```

**Update DbContext**: Add Settings DbSet to ApplicationDbContext
```csharp
public DbSet<Setting> Settings { get; set; }
```

**Dependencies**: Steps 1.2, 1.3  
**Estimated Effort**: 15 minutes

---

### Phase 2: Configuration Loading Logic

#### Step 2.1: Update ScriptEngineConfiguration
**Location**: `src/Automaton.Core.Scripting/ScriptEngineFactory.cs`

**Add Methods to ScriptEngineConfiguration**:
```csharp
public class ScriptEngineConfiguration
{
    // Existing properties...
    public bool UsePythonNet { get; set; } = false;
    public bool EnableDebugMode { get; set; } = false;
    public bool AutoDetectPythonPaths { get; set; } = true;
    public List<string> PythonPaths { get; set; } = new List<string>();
    public string? PythonDllPath { get; set; } = null;
    public int DefaultTimeoutSeconds { get; set; } = 60;
    public int NetworkTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Load configuration from runner-specific settings
    /// User-specified paths take priority over auto-detection
    /// </summary>
    public void LoadFromRunnerSettings(RunnerScriptEngineSettings? settings)
    {
        if (settings?.Python == null) return;

        if (!string.IsNullOrWhiteSpace(settings.Python.PythonDllPath))
        {
            PythonDllPath = settings.Python.PythonDllPath;
            if (EnableDebugMode)
            {
                Console.WriteLine($"Using user-specified Python DLL: {PythonDllPath}");
            }
        }

        if (settings.Python.PythonPaths?.Any() == true)
        {
            PythonPaths = new List<string>(settings.Python.PythonPaths);
            if (EnableDebugMode)
            {
                Console.WriteLine($"Using {PythonPaths.Count} user-specified Python paths");
            }
        }
    }

    /// <summary>
    /// Load configuration from desktop-specific settings
    /// Same logic as LoadFromRunnerSettings but kept separate for clarity
    /// </summary>
    public void LoadFromDesktopSettings(PythonEngineSettings? settings)
    {
        if (settings == null) return;

        if (!string.IsNullOrWhiteSpace(settings.PythonDllPath))
        {
            PythonDllPath = settings.PythonDllPath;
            if (EnableDebugMode)
            {
                Console.WriteLine($"Using user-specified Python DLL: {PythonDllPath}");
            }
        }

        if (settings.PythonPaths?.Any() == true)
        {
            PythonPaths = new List<string>(settings.PythonPaths);
            if (EnableDebugMode)
            {
                Console.WriteLine($"Using {PythonPaths.Count} user-specified Python paths");
            }
        }
    }

    public void AddPythonPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !PythonPaths.Contains(path))
        {
            PythonPaths.Add(path);
        }
    }
}
```

**Dependencies**: Step 1.1  
**Estimated Effort**: 20 minutes

---

#### Step 2.2: Update PythonNetScriptEngine Initialization
**Location**: `src/Automaton.Core.Scripting/PythonNetScriptEngine.cs`

**Modify Constructor/Initialization**:
```csharp
// In PythonNetScriptEngine constructor or initialization method

// Priority 1: Use user-specified Python DLL path
if (!string.IsNullOrWhiteSpace(configuration.PythonDllPath))
{
    if (configuration.EnableDebugMode)
    {
        Console.WriteLine($"Setting Python DLL from user configuration: {configuration.PythonDllPath}");
    }
    Runtime.PythonDLL = configuration.PythonDllPath;
}
else if (configuration.AutoDetectPythonPaths)
{
    // Priority 2: Auto-detect Python DLL
    var detectedDll = DetectPythonDll();
    if (detectedDll != null)
    {
        if (configuration.EnableDebugMode)
        {
            Console.WriteLine($"Auto-detected Python DLL: {detectedDll}");
        }
        Runtime.PythonDLL = detectedDll;
    }
}

// Priority 1: Use user-specified Python paths
if (configuration.PythonPaths?.Any() == true)
{
    if (configuration.EnableDebugMode)
    {
        Console.WriteLine($"Using {configuration.PythonPaths.Count} user-specified Python paths");
    }
    
    using (Py.GIL())
    {
        dynamic sys = Py.Import("sys");
        foreach (var path in configuration.PythonPaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                sys.path.append(path);
                if (configuration.EnableDebugMode)
                {
                    Console.WriteLine($"  Added path: {path}");
                }
            }
        }
    }
}
else if (configuration.AutoDetectPythonPaths)
{
    // Priority 2: Auto-detect Python paths
    var detectedPaths = DetectPythonPaths();
    if (detectedPaths.Any() && configuration.EnableDebugMode)
    {
        Console.WriteLine($"Auto-detected {detectedPaths.Count} Python paths");
    }
    
    using (Py.GIL())
    {
        dynamic sys = Py.Import("sys");
        foreach (var path in detectedPaths)
        {
            sys.path.append(path);
        }
    }
}
```

**Dependencies**: Step 2.1  
**Estimated Effort**: 20 minutes

---

### Phase 3: Runner-Side Implementation

#### Step 3.1: Create Runner Settings Service
**Location**: `src/Automaton.Runner.Console/Services/RunnerSettingsService.cs`

**Create Service**:
```csharp
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Service to load and manage runner-specific script engine settings
/// </summary>
public class RunnerSettingsService
{
    private readonly IRunnerRepository _runnerRepository;
    private readonly ILogger<RunnerSettingsService> _logger;

    public RunnerSettingsService(
        IRunnerRepository runnerRepository, 
        ILogger<RunnerSettingsService> logger)
    {
        _runnerRepository = runnerRepository;
        _logger = logger;
    }

    /// <summary>
    /// Load script engine settings for the specified runner
    /// </summary>
    /// <param name="runnerId">The runner's unique identifier</param>
    /// <returns>Deserialized settings or null if not configured</returns>
    public async Task<RunnerScriptEngineSettings?> LoadSettingsAsync(Guid runnerId)
    {
        try
        {
            var runner = await _runnerRepository.GetByIdAsync(runnerId);
            
            if (runner == null)
            {
                _logger.LogWarning("Runner {RunnerId} not found", runnerId);
                return null;
            }

            if (string.IsNullOrWhiteSpace(runner.ScriptEngineSettings))
            {
                _logger.LogInformation("No script engine settings configured for runner {RunnerId}", runnerId);
                return null;
            }

            var settings = JsonSerializer.Deserialize<RunnerScriptEngineSettings>(
                runner.ScriptEngineSettings,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            _logger.LogInformation("Loaded script engine settings for runner {RunnerId}", runnerId);
            return settings;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize script engine settings for runner {RunnerId}", runnerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading script engine settings for runner {RunnerId}", runnerId);
            return null;
        }
    }
}
```

**Dependencies**: Steps 1.1, 1.2  
**Estimated Effort**: 25 minutes

---

#### Step 3.2: Update Runner Initialization
**Location**: `src/Automaton.Runner.Console/Program.cs` (or startup file)

**Update Startup Logic**:
```csharp
// In ConfigureServices or similar
services.AddScoped<RunnerSettingsService>();

// In runner initialization
public async Task InitializeAsync(Guid runnerId)
{
    var settingsService = serviceProvider.GetRequiredService<RunnerSettingsService>();
    var runnerSettings = await settingsService.LoadSettingsAsync(runnerId);

    var config = new ScriptEngineConfiguration
    {
        UsePythonNet = true,
        EnableDebugMode = true, // Or from environment variable
        AutoDetectPythonPaths = true
    };

    // Load user settings with priority
    config.LoadFromRunnerSettings(runnerSettings);

    var scriptEngineFactory = new ScriptEngineFactory(config);
    
    // Use scriptEngineFactory for script execution
}
```

**Dependencies**: Steps 2.1, 3.1  
**Estimated Effort**: 15 minutes

---

### Phase 4: Desktop-Side Implementation

#### Step 4.1: Create Settings Service
**Location**: `src/Automaton.Studio.Desktop/Services/SettingsService.cs`

**Create Service**:
```csharp
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Studio.Desktop.Services;

/// <summary>
/// Service to manage application settings stored in database
/// </summary>
public class SettingsService
{
    private const string PythonSettingsKey = "Desktop.PythonSettings";
    
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(
        ISettingsRepository settingsRepository,
        ILogger<SettingsService> logger)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get Python engine settings for the desktop environment
    /// </summary>
    public async Task<PythonEngineSettings?> GetPythonSettingsAsync()
    {
        try
        {
            var setting = await _settingsRepository.GetByKeyAsync(PythonSettingsKey);
            
            if (setting == null)
            {
                _logger.LogInformation("No Python settings configured for desktop");
                return null;
            }

            var pythonSettings = JsonSerializer.Deserialize<PythonEngineSettings>(
                setting.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            return pythonSettings;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Python settings");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Python settings");
            return null;
        }
    }

    /// <summary>
    /// Save Python engine settings for the desktop environment
    /// </summary>
    public async Task SavePythonSettingsAsync(PythonEngineSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            var setting = await _settingsRepository.GetByKeyAsync(PythonSettingsKey);
            
            if (setting == null)
            {
                setting = new Setting
                {
                    Key = PythonSettingsKey,
                    Value = json,
                    Description = "Python engine configuration for desktop environment",
                    UpdatedAt = DateTime.UtcNow
                };
                await _settingsRepository.CreateAsync(setting);
            }
            else
            {
                setting.Value = json;
                setting.UpdatedAt = DateTime.UtcNow;
                await _settingsRepository.UpdateAsync(setting);
            }

            _logger.LogInformation("Saved Python settings for desktop");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Python settings");
            throw;
        }
    }
}
```

**Dependencies**: Step 1.3  
**Estimated Effort**: 30 minutes

---

#### Step 4.2: Update Desktop Script Execution
**Location**: Find where Desktop executes scripts (likely in a service or handler)

**Update Execution Logic**:
```csharp
public class DesktopScriptExecutor
{
    private readonly SettingsService _settingsService;

    public async Task<ScriptExecutionResult> ExecuteScriptAsync(string code, IDictionary<string, object> inputs)
    {
        // Load desktop Python settings
        var pythonSettings = await _settingsService.GetPythonSettingsAsync();

        var config = new ScriptEngineConfiguration
        {
            UsePythonNet = true,
            EnableDebugMode = true,
            AutoDetectPythonPaths = true
        };

        // Load user settings with priority
        config.LoadFromDesktopSettings(pythonSettings);

        var scriptEngineFactory = new ScriptEngineFactory(config);
        var engine = scriptEngineFactory.GetEngine("text/x-python");
        
        return engine.ExecuteScript(code, inputs);
    }
}
```

**Dependencies**: Steps 2.1, 4.1  
**Estimated Effort**: 15 minutes

---

### Phase 5: API Layer

#### Step 5.1: Create Runner Settings Endpoints
**Location**: `src/Automaton.Studio.Server/Controllers/RunnersController.cs` (extend existing)

**Add Endpoints**:
```csharp
/// <summary>
/// Get script engine settings for a specific runner
/// </summary>
[HttpGet("{id}/script-settings")]
[ProducesResponseType(typeof(RunnerScriptEngineSettings), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<RunnerScriptEngineSettings>> GetScriptSettings(Guid id)
{
    var runner = await _runnerRepository.GetByIdAsync(id);
    
    if (runner == null)
    {
        return NotFound();
    }

    if (string.IsNullOrWhiteSpace(runner.ScriptEngineSettings))
    {
        return Ok(new RunnerScriptEngineSettings());
    }

    var settings = JsonSerializer.Deserialize<RunnerScriptEngineSettings>(
        runner.ScriptEngineSettings,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    return Ok(settings ?? new RunnerScriptEngineSettings());
}

/// <summary>
/// Update script engine settings for a specific runner
/// </summary>
[HttpPut("{id}/script-settings")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> UpdateScriptSettings(
    Guid id, 
    [FromBody] RunnerScriptEngineSettings settings)
{
    var runner = await _runnerRepository.GetByIdAsync(id);
    
    if (runner == null)
    {
        return NotFound();
    }

    try
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });

        runner.ScriptEngineSettings = json;
        await _runnerRepository.UpdateAsync(runner);

        return NoContent();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating script settings for runner {RunnerId}", id);
        return BadRequest(new { error = "Failed to update settings" });
    }
}
```

**Dependencies**: Steps 1.1, 1.2  
**Estimated Effort**: 25 minutes

---

#### Step 5.2: Create Desktop Settings Endpoints
**Location**: `src/Automaton.Studio.Server/Controllers/SettingsController.cs` (new file)

**Create Controller**:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Automaton.Core.Models;
using Automaton.Studio.Desktop.Services;

namespace Automaton.Studio.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        SettingsService settingsService,
        ILogger<SettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Get Python engine settings for desktop environment
    /// </summary>
    [HttpGet("python")]
    [ProducesResponseType(typeof(PythonEngineSettings), StatusCodes.Status200OK)]
    public async Task<ActionResult<PythonEngineSettings>> GetPythonSettings()
    {
        var settings = await _settingsService.GetPythonSettingsAsync();
        return Ok(settings ?? new PythonEngineSettings());
    }

    /// <summary>
    /// Update Python engine settings for desktop environment
    /// </summary>
    [HttpPut("python")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePythonSettings([FromBody] PythonEngineSettings settings)
    {
        try
        {
            await _settingsService.SavePythonSettingsAsync(settings);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Python settings");
            return BadRequest(new { error = "Failed to update Python settings" });
        }
    }
}
```

**Dependencies**: Steps 1.1, 4.1  
**Estimated Effort**: 25 minutes

---

### Phase 6: UI Implementation (Blazor)

#### Step 6.1: Create Runner Settings Component
**Location**: `src/Automaton.Studio/Components/Runners/RunnerScriptSettings.razor`

**Create Component**:
```razor
@inject HttpClient Http
@inject ILogger<RunnerScriptSettings> Logger

<div class="script-settings-container">
    <h4>Script Engine Settings</h4>
    
    @if (isLoading)
    {
        <div class="spinner-border" role="status">
            <span class="visually-hidden">Loading...</span>
        </div>
    }
    else
    {
        <EditForm Model="@settings" OnValidSubmit="HandleSave">
            <DataAnnotationsValidator />
            
            <div class="card mb-3">
                <div class="card-header">
                    <h5>Python Configuration</h5>
                </div>
                <div class="card-body">
                    <div class="mb-3">
                        <label class="form-label">Python DLL Path</label>
                        <InputText @bind-Value="pythonSettings.PythonDllPath" 
                                   class="form-control" 
                                   placeholder="e.g., C:\Python312\python312.dll" />
                        <div class="form-text">
                            Leave empty to use auto-detection
                        </div>
                    </div>

                    <div class="mb-3">
                        <label class="form-label">Python Package Paths</label>
                        
                        @foreach (var (path, index) in pythonSettings.PythonPaths.Select((p, i) => (p, i)))
                        {
                            <div class="input-group mb-2">
                                <input type="text" 
                                       class="form-control" 
                                       value="@path"
                                       @onchange="@(e => UpdatePath(index, e.Value?.ToString()))" />
                                <button type="button" 
                                        class="btn btn-outline-danger" 
                                        @onclick="() => RemovePath(index)">
                                    <i class="oi oi-trash"></i>
                                </button>
                            </div>
                        }
                        
                        <button type="button" 
                                class="btn btn-outline-primary btn-sm" 
                                @onclick="AddPath">
                            <i class="oi oi-plus"></i> Add Path
                        </button>
                        
                        <div class="form-text">
                            Additional paths to search for Python packages
                        </div>
                    </div>
                </div>
            </div>

            <div class="d-flex gap-2">
                <button type="submit" class="btn btn-primary" disabled="@isSaving">
                    @if (isSaving)
                    {
                        <span class="spinner-border spinner-border-sm me-1"></span>
                    }
                    Save Settings
                </button>
                <button type="button" class="btn btn-secondary" @onclick="HandleCancel">
                    Cancel
                </button>
            </div>
        </EditForm>
        
        @if (!string.IsNullOrEmpty(errorMessage))
        {
            <div class="alert alert-danger mt-3">@errorMessage</div>
        }
        
        @if (showSuccess)
        {
            <div class="alert alert-success mt-3">Settings saved successfully!</div>
        }
    }
</div>

@code {
    [Parameter]
    public Guid RunnerId { get; set; }

    [Parameter]
    public EventCallback OnSaved { get; set; }

    private RunnerScriptEngineSettings settings = new();
    private PythonEngineSettings pythonSettings => settings.Python ??= new();
    
    private bool isLoading = true;
    private bool isSaving = false;
    private bool showSuccess = false;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        await LoadSettings();
    }

    private async Task LoadSettings()
    {
        isLoading = true;
        errorMessage = null;

        try
        {
            settings = await Http.GetFromJsonAsync<RunnerScriptEngineSettings>(
                $"api/runners/{RunnerId}/script-settings"
            ) ?? new RunnerScriptEngineSettings();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading runner settings");
            errorMessage = "Failed to load settings";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task HandleSave()
    {
        isSaving = true;
        showSuccess = false;
        errorMessage = null;

        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/runners/{RunnerId}/script-settings",
                settings
            );

            if (response.IsSuccessStatusCode)
            {
                showSuccess = true;
                await OnSaved.InvokeAsync();
                
                // Hide success message after 3 seconds
                await Task.Delay(3000);
                showSuccess = false;
            }
            else
            {
                errorMessage = "Failed to save settings";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving runner settings");
            errorMessage = "An error occurred while saving settings";
        }
        finally
        {
            isSaving = false;
        }
    }

    private void HandleCancel()
    {
        _ = LoadSettings(); // Reload to reset changes
    }

    private void AddPath()
    {
        pythonSettings.PythonPaths.Add(string.Empty);
    }

    private void RemovePath(int index)
    {
        pythonSettings.PythonPaths.RemoveAt(index);
    }

    private void UpdatePath(int index, string? value)
    {
        if (index >= 0 && index < pythonSettings.PythonPaths.Count)
        {
            pythonSettings.PythonPaths[index] = value ?? string.Empty;
        }
    }
}
```

**Dependencies**: Step 5.1  
**Estimated Effort**: 45 minutes

---

#### Step 6.2: Integrate into Runner Edit Page
**Location**: Find existing Runner edit/details page

**Add Tab or Section**:
```razor
@* In the runner details/edit page *@

<div class="tab-content">
    <div class="tab-pane active" id="general">
        @* Existing general settings *@
    </div>
    
    <div class="tab-pane" id="script-settings">
        <RunnerScriptSettings RunnerId="@RunnerId" OnSaved="@HandleSettingsSaved" />
    </div>
</div>

@code {
    private async Task HandleSettingsSaved()
    {
        // Optional: Refresh runner data or show notification
        await JSRuntime.InvokeVoidAsync("showToast", "Settings saved successfully");
    }
}
```

**Dependencies**: Step 6.1  
**Estimated Effort**: 20 minutes

---

#### Step 6.3: Create Desktop Settings Page
**Location**: `src/Automaton.Studio.Desktop/Pages/Settings.razor`

**Create or Update Page**:
```razor
@page "/settings"
@inject HttpClient Http
@inject ILogger<Settings> Logger

<PageTitle>Settings - Automaton Studio</PageTitle>

<div class="container-fluid">
    <h2>Application Settings</h2>
    
    <div class="row mt-4">
        <div class="col-md-8">
            <div class="card">
                <div class="card-header">
                    <h4>Python Engine Configuration</h4>
                    <small class="text-muted">
                        Configure Python paths for local script execution
                    </small>
                </div>
                <div class="card-body">
                    @if (isLoading)
                    {
                        <div class="text-center">
                            <div class="spinner-border" role="status">
                                <span class="visually-hidden">Loading...</span>
                            </div>
                        </div>
                    }
                    else
                    {
                        <EditForm Model="@pythonSettings" OnValidSubmit="HandleSave">
                            <div class="mb-3">
                                <label class="form-label fw-bold">Python DLL Path</label>
                                <InputText @bind-Value="pythonSettings.PythonDllPath" 
                                           class="form-control form-control-lg" 
                                           placeholder="e.g., C:\Python312\python312.dll" />
                                <div class="form-text">
                                    Path to the Python DLL file. Leave empty to use auto-detection.
                                </div>
                            </div>

                            <div class="mb-3">
                                <label class="form-label fw-bold">Python Package Paths</label>
                                
                                @if (pythonSettings.PythonPaths.Any())
                                {
                                    @foreach (var (path, index) in pythonSettings.PythonPaths.Select((p, i) => (p, i)))
                                    {
                                        <div class="input-group mb-2">
                                            <input type="text" 
                                                   class="form-control" 
                                                   value="@path"
                                                   placeholder="e.g., C:\Python312\Lib\site-packages"
                                                   @onchange="@(e => UpdatePath(index, e.Value?.ToString()))" />
                                            <button type="button" 
                                                    class="btn btn-outline-danger" 
                                                    @onclick="() => RemovePath(index)">
                                                <i class="oi oi-trash"></i> Remove
                                            </button>
                                        </div>
                                    }
                                }
                                else
                                {
                                    <p class="text-muted">No custom paths configured. Click "Add Path" to add one.</p>
                                }
                                
                                <button type="button" 
                                        class="btn btn-outline-primary" 
                                        @onclick="AddPath">
                                    <i class="oi oi-plus"></i> Add Path
                                </button>
                                
                                <div class="form-text mt-2">
                                    Additional directories to search for Python packages. Leave empty to use auto-detection.
                                </div>
                            </div>

                            <hr />

                            <div class="d-flex gap-2">
                                <button type="submit" class="btn btn-primary btn-lg" disabled="@isSaving">
                                    @if (isSaving)
                                    {
                                        <span class="spinner-border spinner-border-sm me-2"></span>
                                    }
                                    <i class="oi oi-check"></i> Save Settings
                                </button>
                                <button type="button" class="btn btn-secondary btn-lg" @onclick="HandleReset">
                                    <i class="oi oi-reload"></i> Reset
                                </button>
                            </div>
                        </EditForm>
                        
                        @if (!string.IsNullOrEmpty(errorMessage))
                        {
                            <div class="alert alert-danger mt-3">
                                <i class="oi oi-warning"></i> @errorMessage
                            </div>
                        }
                        
                        @if (showSuccess)
                        {
                            <div class="alert alert-success mt-3">
                                <i class="oi oi-check"></i> Settings saved successfully!
                            </div>
                        }
                    }
                </div>
            </div>

            <div class="card mt-3">
                <div class="card-body">
                    <h5><i class="oi oi-info"></i> How it works</h5>
                    <ul>
                        <li>Settings configured here apply only to scripts executed in Automaton Studio Desktop</li>
                        <li>Runners have their own separate configuration</li>
                        <li>If no paths are specified, the system will attempt to auto-detect Python installations</li>
                        <li>Custom paths take priority over auto-detection</li>
                    </ul>
                </div>
            </div>
        </div>
    </div>
</div>

@code {
    private PythonEngineSettings pythonSettings = new();
    
    private bool isLoading = true;
    private bool isSaving = false;
    private bool showSuccess = false;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        await LoadSettings();
    }

    private async Task LoadSettings()
    {
        isLoading = true;
        errorMessage = null;

        try
        {
            pythonSettings = await Http.GetFromJsonAsync<PythonEngineSettings>("api/settings/python") 
                             ?? new PythonEngineSettings();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading Python settings");
            errorMessage = "Failed to load settings";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task HandleSave()
    {
        isSaving = true;
        showSuccess = false;
        errorMessage = null;

        try
        {
            var response = await Http.PutAsJsonAsync("api/settings/python", pythonSettings);

            if (response.IsSuccessStatusCode)
            {
                showSuccess = true;
                
                // Hide success message after 3 seconds
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    showSuccess = false;
                    StateHasChanged();
                });
            }
            else
            {
                errorMessage = "Failed to save settings. Please try again.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving Python settings");
            errorMessage = "An error occurred while saving settings";
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task HandleReset()
    {
        await LoadSettings();
        showSuccess = false;
        errorMessage = null;
    }

    private void AddPath()
    {
        pythonSettings.PythonPaths.Add(string.Empty);
    }

    private void RemovePath(int index)
    {
        if (index >= 0 && index < pythonSettings.PythonPaths.Count)
        {
            pythonSettings.PythonPaths.RemoveAt(index);
        }
    }

    private void UpdatePath(int index, string? value)
    {
        if (index >= 0 && index < pythonSettings.PythonPaths.Count)
        {
            pythonSettings.PythonPaths[index] = value ?? string.Empty;
        }
    }
}
```

**Dependencies**: Step 5.2  
**Estimated Effort**: 45 minutes

---

### Phase 7: Testing & Validation

#### Step 7.1: Update Unit Tests
**Location**: `src/UnitTests/Automaton.Core.Tests/Scripting/ScriptEngineConfigurationTests.cs`

**Add Tests**:
```csharp
[TestClass]
public class ScriptEngineConfigurationTests
{
    [TestMethod]
    public void LoadFromRunnerSettings_WithPythonDllPath_ShouldSetDllPath()
    {
        // Arrange
        var config = new ScriptEngineConfiguration();
        var settings = new RunnerScriptEngineSettings
        {
            Python = new PythonEngineSettings
            {
                PythonDllPath = @"C:\Python312\python312.dll"
            }
        };

        // Act
        config.LoadFromRunnerSettings(settings);

        // Assert
        Assert.AreEqual(@"C:\Python312\python312.dll", config.PythonDllPath);
    }

    [TestMethod]
    public void LoadFromRunnerSettings_WithPythonPaths_ShouldSetPaths()
    {
        // Arrange
        var config = new ScriptEngineConfiguration();
        var settings = new RunnerScriptEngineSettings
        {
            Python = new PythonEngineSettings
            {
                PythonPaths = new List<string> 
                { 
                    @"C:\Python312\Lib\site-packages",
                    @"C:\CustomPackages"
                }
            }
        };

        // Act
        config.LoadFromRunnerSettings(settings);

        // Assert
        Assert.AreEqual(2, config.PythonPaths.Count);
        CollectionAssert.Contains(config.PythonPaths, @"C:\Python312\Lib\site-packages");
        CollectionAssert.Contains(config.PythonPaths, @"C:\CustomPackages");
    }

    [TestMethod]
    public void LoadFromRunnerSettings_WithNullSettings_ShouldNotThrow()
    {
        // Arrange
        var config = new ScriptEngineConfiguration();

        // Act & Assert
        config.LoadFromRunnerSettings(null); // Should not throw
    }

    [TestMethod]
    public void LoadFromDesktopSettings_WithPythonSettings_ShouldApplySettings()
    {
        // Arrange
        var config = new ScriptEngineConfiguration();
        var settings = new PythonEngineSettings
        {
            PythonDllPath = @"C:\Python312\python312.dll",
            PythonPaths = new List<string> { @"C:\Python312\Lib\site-packages" }
        };

        // Act
        config.LoadFromDesktopSettings(settings);

        // Assert
        Assert.AreEqual(@"C:\Python312\python312.dll", config.PythonDllPath);
        Assert.AreEqual(1, config.PythonPaths.Count);
    }

    [TestMethod]
    public void UserSettings_ShouldTakePriorityOverDefaults()
    {
        // Arrange
        var config = new ScriptEngineConfiguration
        {
            PythonDllPath = "auto-detected-path.dll"
        };
        
        var settings = new RunnerScriptEngineSettings
        {
            Python = new PythonEngineSettings
            {
                PythonDllPath = "user-specified-path.dll"
            }
        };

        // Act
        config.LoadFromRunnerSettings(settings);

        // Assert
        Assert.AreEqual("user-specified-path.dll", config.PythonDllPath);
    }
}
```

**Dependencies**: Phase 2  
**Estimated Effort**: 30 minutes

---

#### Step 7.2: Integration Tests
**Location**: `src/UnitTests/Automaton.Studio.Tests/Integration/ScriptEngineConfigurationIntegrationTests.cs`

**Add Tests**:
```csharp
[TestClass]
public class ScriptEngineConfigurationIntegrationTests
{
    private TestServer _server;
    private HttpClient _client;

    [TestInitialize]
    public async Task Initialize()
    {
        // Setup test server and database
        _server = CreateTestServer();
        _client = _server.CreateClient();
    }

    [TestMethod]
    public async Task Runner_WithCustomPythonSettings_ShouldExecuteScript()
    {
        // Arrange
        var runnerId = Guid.NewGuid();
        var settings = new RunnerScriptEngineSettings
        {
            Python = new PythonEngineSettings
            {
                PythonDllPath = GetTestPythonDllPath(),
                PythonPaths = new List<string> { GetTestPythonSitePackages() }
            }
        };

        // Save settings
        var response = await _client.PutAsJsonAsync(
            $"/api/runners/{runnerId}/script-settings",
            settings
        );
        Assert.IsTrue(response.IsSuccessStatusCode);

        // Act
        var scriptResult = await ExecuteScriptOnRunner(runnerId, "print('Hello')");

        // Assert
        Assert.IsTrue(scriptResult.Success);
        Assert.IsTrue(scriptResult.StandardOutput.Contains("Hello"));
    }

    [TestMethod]
    public async Task Desktop_WithCustomPythonSettings_ShouldExecuteScript()
    {
        // Arrange
        var settings = new PythonEngineSettings
        {
            PythonDllPath = GetTestPythonDllPath(),
            PythonPaths = new List<string> { GetTestPythonSitePackages() }
        };

        // Save settings
        var response = await _client.PutAsJsonAsync("/api/settings/python", settings);
        Assert.IsTrue(response.IsSuccessStatusCode);

        // Act
        var scriptResult = await ExecuteScriptInDesktop("print('Hello from Desktop')");

        // Assert
        Assert.IsTrue(scriptResult.Success);
        Assert.IsTrue(scriptResult.StandardOutput.Contains("Hello from Desktop"));
    }

    [TestMethod]
    public async Task EmptySettings_ShouldFallbackToAutoDetection()
    {
        // Arrange
        var runnerId = Guid.NewGuid();
        var emptySettings = new RunnerScriptEngineSettings();

        await _client.PutAsJsonAsync(
            $"/api/runners/{runnerId}/script-settings",
            emptySettings
        );

        // Act
        var scriptResult = await ExecuteScriptOnRunner(runnerId, "import sys; print(sys.version)");

        // Assert
        Assert.IsTrue(scriptResult.Success);
        Assert.IsTrue(scriptResult.StandardOutput.Length > 0);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _server?.Dispose();
    }

    private string GetTestPythonDllPath()
    {
        // Return actual test Python DLL path
        return @"C:\Python312\python312.dll";
    }

    private string GetTestPythonSitePackages()
    {
        return @"C:\Python312\Lib\site-packages";
    }

    private async Task<ScriptExecutionResult> ExecuteScriptOnRunner(Guid runnerId, string code)
    {
        // Implementation to execute script on runner
        throw new NotImplementedException();
    }

    private async Task<ScriptExecutionResult> ExecuteScriptInDesktop(string code)
    {
        // Implementation to execute script in desktop
        throw new NotImplementedException();
    }
}
```

**Dependencies**: Phases 3, 4  
**Estimated Effort**: 40 minutes

---

#### Step 7.3: Manual Testing Checklist

**Test Scenarios**:

1. **Runner Configuration**
   - [ ] Navigate to Runner details page
   - [ ] Open Script Settings tab
   - [ ] Add Python DLL path
   - [ ] Add 2-3 Python package paths
   - [ ] Save settings
   - [ ] Verify settings persist after page refresh
   - [ ] Execute a script that uses an external package
   - [ ] Verify script executes successfully

2. **Desktop Configuration**
   - [ ] Navigate to Settings page
   - [ ] Configure Python DLL path
   - [ ] Add Python package paths
   - [ ] Save settings
   - [ ] Restart application
   - [ ] Verify settings persist
   - [ ] Execute a local script with external package
   - [ ] Verify script executes successfully

3. **Auto-Detection Fallback**
   - [ ] Clear all Python settings for a runner
   - [ ] Execute a basic Python script
   - [ ] Verify auto-detection works
   - [ ] Check console logs for auto-detection messages

4. **Invalid Paths**
   - [ ] Configure invalid Python DLL path
   - [ ] Attempt to execute script
   - [ ] Verify meaningful error message
   - [ ] Configure valid path
   - [ ] Verify script now executes

5. **Multiple Runners**
   - [ ] Configure different Python paths for Runner A and Runner B
   - [ ] Execute same script on both runners
   - [ ] Verify each uses its configured paths

**Dependencies**: Phase 6  
**Estimated Effort**: 1 hour

---

## Data Models

### JSON Structure Examples

#### Runner Settings (stored in `Runner.ScriptEngineSettings`)
```json
{
  "python": {
    "pythonDllPath": "C:\\Python312\\python312.dll",
    "pythonPaths": [
      "C:\\Python312\\Lib\\site-packages",
      "C:\\CustomPackages",
      "D:\\SharedPythonModules"
    ]
  }
}
```

#### Desktop Settings (stored in `Settings` table, key: "Desktop.PythonSettings")
```json
{
  "pythonDllPath": "C:\\Python311\\python311.dll",
  "pythonPaths": [
    "C:\\Python311\\Lib\\site-packages",
    "C:\\Users\\Developer\\PythonProjects"
  ]
}
```

### Database Schema

#### Runner Table (modified)
```sql
ALTER TABLE Runners
ADD ScriptEngineSettings NVARCHAR(MAX) NULL;
```

#### Settings Table (new)
```sql
CREATE TABLE Settings (
    [Key] NVARCHAR(255) PRIMARY KEY,
    [Value] NVARCHAR(MAX) NOT NULL,
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Description NVARCHAR(500) NULL
);

CREATE INDEX IX_Settings_Key ON Settings([Key]);
```

---

## API Specifications

### Runner Settings Endpoints

#### GET /api/runners/{id}/script-settings
**Description**: Get script engine settings for a specific runner

**Response 200**:
```json
{
  "python": {
    "pythonDllPath": "C:\\Python312\\python312.dll",
    "pythonPaths": [
      "C:\\Python312\\Lib\\site-packages"
    ]
  }
}
```

#### PUT /api/runners/{id}/script-settings
**Description**: Update script engine settings for a runner

**Request Body**:
```json
{
  "python": {
    "pythonDllPath": "C:\\Python312\\python312.dll",
    "pythonPaths": [
      "C:\\Python312\\Lib\\site-packages",
      "C:\\CustomPackages"
    ]
  }
}
```

**Response**: 204 No Content

---

### Desktop Settings Endpoints

#### GET /api/settings/python
**Description**: Get Python engine settings for desktop

**Response 200**:
```json
{
  "pythonDllPath": "C:\\Python311\\python311.dll",
  "pythonPaths": [
    "C:\\Python311\\Lib\\site-packages"
  ]
}
```

#### PUT /api/settings/python
**Description**: Update Python engine settings for desktop

**Request Body**:
```json
{
  "pythonDllPath": "C:\\Python311\\python311.dll",
  "pythonPaths": [
    "C:\\Python311\\Lib\\site-packages",
    "C:\\Users\\Developer\\PythonProjects"
  ]
}
```

**Response**: 204 No Content

---

## UI/UX Design

### Runner Settings UI

**Location**: Runner Details ? Script Settings Tab

**Features**:
- Input field for Python DLL path with placeholder
- Dynamic list for Python paths (add/remove buttons)
- Form validation
- Save/Cancel buttons
- Loading states
- Success/Error notifications
- Help text explaining auto-detection

### Desktop Settings UI

**Location**: Settings ? Python Configuration Section

**Features**:
- Card-based layout
- Input field for Python DLL path
- Dynamic list for Python paths
- Save/Reset buttons
- Visual feedback (success/error alerts)
- Informational section explaining how it works
- Help text for each field

---

## Testing Strategy

### Unit Tests
- Configuration loading methods
- Priority logic (user settings > auto-detection)
- Path validation
- JSON serialization/deserialization

### Integration Tests
- Runner settings persistence
- Desktop settings persistence
- Script execution with custom settings
- Fallback to auto-detection
- Multiple runners with different settings

### Manual Tests
- UI interaction and validation
- Settings persistence across sessions
- Script execution with external packages
- Error handling for invalid paths
- Cross-runner configuration isolation

---

## Risks & Mitigations

### Risk 1: Invalid Paths Breaking Script Execution
**Mitigation**: 
- Validate paths on save (optional feature)
- Provide clear error messages
- Allow fallback to auto-detection
- Add "Test Configuration" button (future enhancement)

### Risk 2: Settings Not Syncing Between UI and Execution
**Mitigation**:
- Ensure settings are loaded at execution time
- Add integration tests covering this scenario
- Log configuration used during execution

### Risk 3: Database Migration Issues
**Mitigation**:
- Test migrations on copy of production data
- Ensure nullable fields (backward compatibility)
- Provide rollback script

### Risk 4: Performance Impact of JSON Deserialization
**Mitigation**:
- Cache deserialized settings
- Settings are small (< 1KB typically)
- Only loaded at script initialization, not per-execution

### Risk 5: User Confusion About Auto-Detection
**Mitigation**:
- Clear help text in UI
- Show auto-detected paths in UI (read-only)
- Documentation explaining priority order

---

## Future Extensibility

### Additional Script Engines

The nested structure allows easy addition of new engines:

```csharp
public class RunnerScriptEngineSettings 
{
    public PythonEngineSettings? Python { get; set; }
    public NodeJsEngineSettings? NodeJs { get; set; }
    public BashEngineSettings? Bash { get; set; }
    public PowerShellEngineSettings? PowerShell { get; set; }
}

public class NodeJsEngineSettings
{
    public string? NodeExecutablePath { get; set; }
    public List<string> NodeModulePaths { get; set; } = new();
    public string? NpmRegistry { get; set; }
}
```

### Additional Features

1. **Test Configuration Button**: Validate paths before saving
2. **Auto-Detection Display**: Show detected paths in UI
3. **Import/Export Settings**: Share configuration between runners
4. **Environment Variables**: Support ${VAR} syntax in paths
5. **Virtual Environment Support**: Auto-detect and use venv
6. **Package Manager Integration**: Install packages from UI

---

## Appendix

### Configuration Flow Diagram

```
????????????????
? User Changes ?
?  Settings    ?
?   in UI      ?
????????????????
       ?
       ?
???????????????????
?  API Endpoint   ?
?  (PUT request)  ?
???????????????????
       ?
       ?
????????????????????
?   Serialize to   ?
?   JSON string    ?
????????????????????
       ?
       ?
????????????????????
?  Save to DB      ?
?  (Runner or      ?
?   Settings)      ?
????????????????????
       ?
       ?
????????????????????
?  Runner/Desktop  ?
?  loads settings  ?
?  at startup      ?
????????????????????
       ?
       ?
????????????????????
?  Deserialize     ?
?  from JSON       ?
????????????????????
       ?
       ?
????????????????????
?  Apply to        ?
?  ScriptEngine    ?
?  Configuration   ?
????????????????????
       ?
       ?
????????????????????
?  Initialize      ?
?  Script Engine   ?
?  with config     ?
????????????????????
```

### Key Decision Points

1. **Why nested JSON structure?**
   - Future-proof for multiple script engines
   - Avoids schema changes when adding engines
   - Clear separation of engine-specific settings

2. **Why separate Desktop and Runner settings?**
   - Different environments have different Python installations
   - Desktop is single-user, runners are multi-tenant
   - Allows per-runner customization

3. **Why priority: User Settings ? Auto-Detection?**
   - User knows their environment best
   - Auto-detection is fallback for ease-of-use
   - Flexibility for advanced users

4. **Why Settings table instead of config files?**
   - Consistent with web application architecture
   - UI-based configuration (no file system access needed)
   - Easy to backup with database
   - Multi-environment support

---

## Glossary

- **Runner**: Remote execution agent that runs automation scripts
- **Desktop**: Local Automaton Studio application for development
- **Auto-Detection**: Automatic discovery of Python installations
- **Script Engine**: Component that executes scripts (Python, Batch, etc.)
- **Python.NET**: Library allowing Python integration with .NET
- **IronPython**: Python implementation for .NET (legacy)

---

**Document Status**: Ready for Review  
**Next Steps**: Review with team ? Approval ? Begin Phase 1 Implementation
