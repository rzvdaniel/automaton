# Python Paths Configuration for Automaton Runners - Design Document

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [Current Architecture Analysis](#current-architecture-analysis)
3. [Recommended Approach](#recommended-approach)
4. [Detailed Architecture Diagrams](#detailed-architecture-diagrams)
5. [Complete Data Models and DTOs](#complete-data-models-and-dtos)
6. [API Endpoint Specifications](#api-endpoint-specifications)
7. [SignalR Communication Protocol](#signalr-communication-protocol)
8. [UI Mockups and Component Structure](#ui-mockups-and-component-structure)
9. [Database Migration Strategy](#database-migration-strategy)
10. [Security Considerations](#security-considerations)
11. [Testing Strategy](#testing-strategy)
12. [Documentation Requirements](#documentation-requirements)
13. [Implementation Phases](#implementation-phases)
14. [Pros and Cons Analysis](#pros-and-cons-analysis)
15. [Conclusion](#conclusion)

---

## Problem Statement

The `Automaton.Core.Scripting.ScriptEngineFactory` uses `ScriptEngineConfiguration` which has properties to configure Python paths for the PythonNet library. These paths can vary per machine where `Automaton.Runner.Console` is running.

### Current Challenges

1. **No per-runner configuration**: Python paths cannot be configured differently for each runner instance
2. **No UI management**: Users cannot configure Python paths through the Studio interface
3. **No validation**: Paths cannot be validated on the actual runner machine before execution
4. **Local execution gaps**: No way to configure Python paths for local Studio execution

### Requirements

Users need a way to configure Python paths:

1. For each runner (different machines have different Python installations)
2. For local development machines when executing flows/steps from Studio UI
3. With validation on the actual runner machine
4. Through both UI and CLI interfaces

---

## Current Architecture Analysis

### 1. ScriptEngineConfiguration

**Location**: `Automaton.Core.Scripting/ScriptEngineFactory.cs`

```csharp
public class ScriptEngineConfiguration
{
    public bool UsePythonNet { get; set; } = false;
    public bool EnableDebugMode { get; set; } = false;
    public bool AutoDetectPythonPaths { get; set; } = true;
    public List<string> PythonPaths { get; set; } = new List<string>();
    public string? PythonDllPath { get; set; } = null;
    public int DefaultTimeoutSeconds { get; set; } = 60;
    public int NetworkTimeoutSeconds { get; set; } = 30;
}
```

**Properties**:
- `PythonPaths`: List of additional Python library paths
- `PythonDllPath`: Path to Python DLL for Python.NET
- `AutoDetectPythonPaths`: Enable automatic Python path detection

### 2. Runner Entity

**Location**: `Automaton.Studio.Server/Entities/Runner.cs`

```csharp
public class Runner
{
    [Required]
    public Guid Id { get; set; }
    [Required]
    public string Name { get; set; }
    public string ConnectionId { get; set; }
    
    public virtual IEnumerable<RunnerUser> RunnerUsers { get; set; }
}
```

**Current State**: Only stores basic runner information (Id, Name, ConnectionId)

### 3. FileStorageService

**Location**: `Automaton.Runner.Console/Services/FileStorageService.cs`

**Current Storage**:
- API Key: `~/.config/AutomatonRunner/apikey.json`
- Runner Config: `~/.config/AutomatonRunner/runner.json`
- Server Config: `~/.config/AutomatonRunner/server.json`

**Current runner.json Structure**:
```json
{
  "RunnerId": "guid",
  "RunnerName": "string",
  "RegisteredAt": "datetime"
}
```

### 4. Configuration Flow

**Runner Console**:
```csharp
services.AddScripting(); // Reads from appsettings.json "Scripting" section
```

**Studio Server**:
```csharp
services.AddScripting(); // Reads from appsettings.json "Scripting" section
```

**Current Limitation**: Configuration is static, read only from appsettings.json at startup.

---

## Recommended Approach

### Hybrid Configuration Strategy

Combine both database-backed UI management and local file-based runner configuration for maximum flexibility.

#### Three Configuration Sources

1. **Runner Local Config (Primary Source)**
   - Location: `~/.config/AutomatonRunner/runner.json`
   - Authority: Runner has final say on its configuration
   - Validation: Paths validated on actual machine
   - Flexibility: Can be edited via CLI or synced from UI

2. **Database (Secondary/UI Management)**
   - Entity: Extended Runner entity with Python configuration fields
   - Purpose: Provides UI management capability, serves as defaults
   - Sync: Two-way synchronization via SignalR

3. **Local Studio Settings (For Studio Execution)**
   - Storage: Browser LocalStorage or User Preferences table
   - Purpose: For executing flows/steps locally from Studio UI
   - Scope: Per-user configuration

#### Configuration Priority

**For Runners**:
1. Local `runner.json` file (user-edited or UI-synced)
2. Environment variables (`AUTOMATON_PYTHON_DLL`, `AUTOMATON_PYTHON_PATHS`)
3. Command-line arguments (during registration)
4. Auto-detection (if enabled)

**For Studio Local Execution**:
1. User preferences (LocalStorage or database UserSettings table)
2. `appsettings.json` Scripting section
3. Auto-detection (if enabled)

---

## Detailed Architecture Diagrams

### System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Automaton Studio (Web/Desktop)                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌──────────────────┐         ┌────────────────────┐                   │
│  │  RunnersPage     │         │   SettingsPage     │                   │
│  │  ┌────────────┐  │         │  ┌──────────────┐  │                   │
│  │  │ Configure  │  │         │  │ Local Python │  │                   │
│  │  │ Runner     │  │         │  │ Config       │  │                   │
│  │  │ Python     │  │         │  └──────────────┘  │                   │
│  │  └────────────┘  │         │                    │                   │
│  └──────────────────┘         └────────────────────┘                   │
│         │                               │                               │
│         │ SignalR/API                   │ LocalStorage/API              │
│         ▼                               ▼                               │
└─────────────────────────────────────────────────────────────────────────┘
         │                               │
         │                               │
         ▼                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        Automaton Studio Server                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌───────────────────────┐         ┌──────────────────────┐            │
│  │  RunnersController    │         │   AutomatonHub       │            │
│  │  ┌─────────────────┐  │         │  ┌────────────────┐  │            │
│  │  │ GetPythonConfig │  │         │  │ UpdatePython   │  │            │
│  │  │ UpdatePython    │  │         │  │ Config         │  │            │
│  │  │ ValidatePaths   │  │         │  │ ValidatePaths  │  │            │
│  │  └─────────────────┘  │         │  └────────────────┘  │            │
│  └───────────────────────┘         └──────────────────────┘            │
│         │                               │                               │
│         │                               │ SignalR Messages              │
│         ▼                               ▼                               │
│  ┌───────────────────────────────────────────────────┐                 │
│  │           Database (MySQL/SQLite)                 │                 │
│  │  ┌────────────────────────────────────────────┐   │                 │
│  │  │ Runner Table                               │   │                 │
│  │  │  - Id, Name, ConnectionId                  │   │                 │
│  │  │  - PythonDllPath                           │   │                 │
│  │  │  - PythonPathsJson                         │   │                 │
│  │  │  - AutoDetectPythonPaths                   │   │                 │
│  │  └────────────────────────────────────────────┘   │                 │
│  └───────────────────────────────────────────────────┘                 │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
                               │
                               │ SignalR Connection
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      Automaton Runner Console                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────────┐         ┌──────────────────────┐               │
│  │  HubService        │         │ FileStorageService   │               │
│  │  ┌──────────────┐  │         │  ┌────────────────┐  │               │
│  │  │ SignalR      │  │         │  │ runner.json    │  │               │
│  │  │ Handlers     │  │         │  │ {              │  │               │
│  │  │ - Update     │  │───────▶ │  │   PythonConfig │  │               │
│  │  │ - Validate   │  │         │  │ }              │  │               │
│  │  └──────────────┘  │         │  └────────────────┘  │               │
│  └────────────────────┘         └──────────────────────┘               │
│         │                               │                               │
│         │                               │                               │
│         ▼                               ▼                               │
│  ┌────────────────────────────────────────────────────┐                │
│  │       ConfigurationService                         │                │
│  │  - Reads Python config from multiple sources       │                │
│  │  - Priority: Local file → Env vars → CLI → Auto   │                │
│  └────────────────────────────────────────────────────┘                │
│         │                                                                │
│         ▼                                                                │
│  ┌────────────────────────────────────────────────────┐                │
│  │       ScriptEngineFactory                          │                │
│  │  - Creates PythonNetScriptEngine                   │                │
│  │  - Uses ScriptEngineConfiguration                  │                │
│  └────────────────────────────────────────────────────┘                │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

### Configuration Flow Sequence Diagram

```
User (UI)          Studio Server       Database         Runner Console      File System
    │                    │                 │                   │                  │
    │  Configure Python  │                 │                   │                  │
    │───────────────────▶│                 │                   │                  │
    │                    │                 │                   │                  │
    │                    │  Save to DB     │                   │                  │
    │                    │────────────────▶│                   │                  │
    │                    │                 │                   │                  │
    │                    │  SignalR: UpdatePythonConfig        │                  │
    │                    │────────────────────────────────────▶│                  │
    │                    │                 │                   │                  │
    │                    │                 │                   │  Validate Paths  │
    │                    │                 │                   │─────────────────▶│
    │                    │                 │                   │                  │
    │                    │                 │                   │  Save to JSON    │
    │                    │                 │                   │─────────────────▶│
    │                    │                 │                   │                  │
    │                    │  SignalR: PythonConfigUpdated       │                  │
    │                    │◀────────────────────────────────────│                  │
    │                    │                 │                   │                  │
    │  Success Response  │                 │                   │                  │
    │◀───────────────────│                 │                   │                  │
    │                    │                 │                   │                  │
```

### Path Validation Flow

```
User (UI)          Studio Server         Runner Console         File System
    │                    │                      │                     │
    │  Validate Paths    │                      │                     │
    │───────────────────▶│                      │                     │
    │                    │                      │                     │
    │                    │  SignalR: ValidatePythonPaths             │
    │                    │─────────────────────▶│                     │
    │                    │                      │                     │
    │                    │                      │  Check DLL exists   │
    │                    │                      │────────────────────▶│
    │                    │                      │                     │
    │                    │                      │  Check paths exist  │
    │                    │                      │────────────────────▶│
    │                    │                      │                     │
    │                    │  SignalR: PythonPathsValidated            │
    │                    │◀─────────────────────│                     │
    │                    │                      │                     │
    │  Validation Result │                      │                     │
    │◀───────────────────│                      │                     │
    │                    │                      │                     │
```

---

## Complete Data Models and DTOs

### 1. Database Entity Extension

**File**: `Automaton.Studio.Server/Entities/Runner.cs`

```csharp
#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

public class Runner
{
    [Required]
    public Guid Id { get; set; }
    
    [Required]
    public string Name { get; set; }
    
    public string ConnectionId { get; set; }

    // Python Configuration Properties
    public string? PythonDllPath { get; set; }
    
    public string? PythonPathsJson { get; set; } // JSON array: ["path1", "path2"]
    
    public bool AutoDetectPythonPaths { get; set; } = true;

    public virtual IEnumerable<RunnerUser> RunnerUsers { get; set; }
}
```

### 2. Python Configuration DTO

**File**: `Automaton.Studio.Server/Models/RunnerPythonConfig.cs`

```csharp
namespace Automaton.Studio.Server.Models;

/// <summary>
/// Python configuration for a runner
/// </summary>
public class RunnerPythonConfig
{
    /// <summary>
    /// Path to Python DLL (e.g., C:\Python311\python311.dll or /usr/lib/libpython3.11.so)
    /// </summary>
    public string? PythonDllPath { get; set; }
    
    /// <summary>
    /// Additional Python paths for libraries and modules
    /// </summary>
    public List<string> PythonPaths { get; set; } = new();
    
    /// <summary>
    /// Enable automatic Python path detection
    /// </summary>
    public bool AutoDetectPythonPaths { get; set; } = true;
}
```

### 3. Update Request DTO

**File**: `Automaton.Studio.Server/Models/UpdateRunnerPythonConfigRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to update runner Python configuration
/// </summary>
public class UpdateRunnerPythonConfigRequest
{
    [Required]
    public Guid RunnerId { get; set; }
    
    [Required]
    public RunnerPythonConfig PythonConfig { get; set; } = new();
}
```

### 4. Validation Request DTO

**File**: `Automaton.Studio.Server/Models/ValidatePythonPathsRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to validate Python paths on runner machine
/// </summary>
public class ValidatePythonPathsRequest
{
    [Required]
    public Guid RunnerId { get; set; }
    
    public List<string> Paths { get; set; } = new();
    
    public string? PythonDllPath { get; set; }
}
```

### 5. Validation Response DTO

**File**: `Automaton.Studio.Server/Models/ValidatePythonPathsResponse.cs`

```csharp
namespace Automaton.Studio.Server.Models;

/// <summary>
/// Response from Python path validation
/// </summary>
public class ValidatePythonPathsResponse
{
    public bool IsValid { get; set; }
    
    public List<PathValidationResult> Results { get; set; } = new();
    
    public string? Message { get; set; }
}

/// <summary>
/// Validation result for a single path
/// </summary>
public class PathValidationResult
{
    public string Path { get; set; } = string.Empty;
    
    public bool Exists { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public string? Type { get; set; } // "dll", "directory", "file"
}
```

### 6. Runner Details Extension

**File**: `Automaton.Studio.Server/Models/RunnerDetails.cs`

```csharp
using Automaton.Core.Enums;

namespace Automaton.Studio.Server.Models;

public class RunnerDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? ConnectionId { get; set; }
    public RunnerStatus Status { get; set; }
    
    // Python Configuration
    public string? PythonDllPath { get; set; }
    public List<string> PythonPaths { get; set; } = new();
    public bool AutoDetectPythonPaths { get; set; } = true;
}
```

### 7. UI Model Extension

**File**: `Automaton.Studio/Models/RunnerModel.cs`

```csharp
using Automaton.Core.Enums;

namespace Automaton.Studio.Models;

public class RunnerModel
{
    // Existing properties
    private readonly Dictionary<RunnerStatus, string> StatusClasses = new()
    {
        { RunnerStatus.None, "badge badge-none" },
        { RunnerStatus.Online, "badge badge-success" },
        { RunnerStatus.Offline,"badge badge-error" },
    };

    public Guid Id { get; set; }
    public string Name { get; set; }
    public string UserId { get; set; }
    public string ConnectionId { get; set; }
    public RunnerStatus Status { get; set; } = RunnerStatus.None;

    public string StatusClass
    {
        get
        {
            return StatusClasses.ContainsKey(Status) ?
                StatusClasses[Status] :
                StatusClasses[RunnerStatus.None];
        }
    }
    
    // Python Configuration Properties
    public string? PythonDllPath { get; set; }
    public List<string> PythonPaths { get; set; } = new();
    public bool AutoDetectPythonPaths { get; set; } = true;
}
```

### 8. Runner Console Configuration Model

**File**: `Automaton.Runner.Console/Models/RunnerPythonConfig.cs` (new file)

```csharp
namespace Automaton.Runner.Console.Models;

/// <summary>
/// Python configuration stored in runner.json
/// </summary>
public class RunnerPythonConfig
{
    public string? PythonDllPath { get; set; }
    public List<string> PythonPaths { get; set; } = new();
    public bool AutoDetectPythonPaths { get; set; } = true;
}
```


---

## API Endpoint Specifications

### RunnersController Extensions

**File**: `Automaton.Studio.Server/Controllers/RunnersController.cs`

#### 1. Get Python Configuration

```csharp
/// <summary>
/// Get Python configuration for a specific runner
/// </summary>
/// <param name="id">Runner ID</param>
/// <returns>Python configuration</returns>
[HttpGet("{id}/python-config")]
[Authorize]
[ProducesResponseType(typeof(RunnerPythonConfig), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public async Task<IActionResult> GetPythonConfig(Guid id)
{
    // Verify user owns the runner
    var userId = GetUserId();
    var runner = await runnerService.GetRunnerByIdAsync(id, userId);
    
    if (runner == null)
        return NotFound();
    
    var config = new RunnerPythonConfig
    {
        PythonDllPath = runner.PythonDllPath,
        PythonPaths = string.IsNullOrEmpty(runner.PythonPathsJson) 
            ? new List<string>() 
            : JsonSerializer.Deserialize<List<string>>(runner.PythonPathsJson) ?? new List<string>(),
        AutoDetectPythonPaths = runner.AutoDetectPythonPaths
    };
    
    return Ok(config);
}
```

#### 2. Update Python Configuration

```csharp
/// <summary>
/// Update Python configuration for a runner
/// </summary>
/// <param name="id">Runner ID</param>
/// <param name="config">New Python configuration</param>
/// <returns>Success result</returns>
[HttpPut("{id}/python-config")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public async Task<IActionResult>UpdatePythonConfig(
    Guid id, 
    [FromBody] RunnerPythonConfig config)
{
    // Verify user owns the runner
    var userId = GetUserId();
    var runner = await runnerService.GetRunnerByIdAsync(id, userId);
    
    if (runner == null)
        return NotFound();
    
    // Update database
    runner.PythonDllPath = config.PythonDllPath;
    runner.PythonPathsJson = JsonSerializer.Serialize(config.PythonPaths);
    runner.AutoDetectPythonPaths = config.AutoDetectPythonPaths;
    
    await runnerService.UpdateRunnerAsync(runner);
    
    // Send SignalR command to runner to update local config
    if (!string.IsNullOrEmpty(runner.ConnectionId))
    {
        await hubContext.Clients.Client(runner.ConnectionId)
            .SendAsync("UpdatePythonConfig", config);
    }
    
    return Ok(new { Message = "Python configuration updated successfully" });
}
```

#### 3. Validate Python Paths

```csharp
/// <summary>
/// Validate Python paths on the runner machine
/// </summary>
/// <param name="id">Runner ID</param>
/// <param name="request">Validation request</param>
/// <returns>Validation results</returns>
[HttpPost("{id}/validate-python-paths")]
[Authorize]
[ProducesResponseType(typeof(ValidatePythonPathsResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
public async Task<IActionResult> ValidatePythonPaths(
    Guid id,
    [FromBody] ValidatePythonPathsRequest request)
{
    // Verify user owns the runner
    var userId = GetUserId();
    var runner = await runnerService.GetRunnerByIdAsync(id, userId);
    
    if (runner == null)
        return NotFound();
    
    // Check if runner is online
    if (string.IsNullOrEmpty(runner.ConnectionId))
        return StatusCode(503, new { Message = "Runner is offline" });
    
    // Send validation request via SignalR and wait for response
    var validationId = Guid.NewGuid().ToString();
    var tcs = new TaskCompletionSource<ValidatePythonPathsResponse>();
    
    // Store completion source for callback
    validationCallbacks[validationId] = tcs;
    
    try
    {
        await hubContext.Clients.Client(runner.ConnectionId)
            .SendAsync("ValidatePythonPaths", new 
            { 
                ValidationId = validationId,
                Paths = request.Paths,
                PythonDllPath = request.PythonDllPath
            });
        
        // Wait for response with timeout
        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return Ok(response);
    }
    catch (TimeoutException)
    {
        return StatusCode(503, new { Message = "Validation timeout" });
    }
    finally
    {
        validationCallbacks.TryRemove(validationId, out _);
    }
}
```

---

## SignalR Communication Protocol

### Hub Methods

**File**: `Automaton.Studio.Server/Hubs/AutomatonHubMethods.cs`

```csharp
namespace Automaton.Studio.Server.Hubs;

public static class AutomatonHubMethods
{
    // Existing methods
    public const string WelcomeRunner = "WelcomeRunner";
    public const string ExecuteFlow = "ExecuteFlow";
    public const string ExecuteCustomStep = "ExecuteCustomStep";
    
    // Python Configuration Methods
    public const string UpdatePythonConfig = "UpdatePythonConfig";
    public const string ValidatePythonPaths = "ValidatePythonPaths";
    public const string PythonConfigUpdated = "PythonConfigUpdated";
    public const string PythonPathsValidated = "PythonPathsValidated";
}
```

### Message Formats

#### 1. UpdatePythonConfig (Server → Runner)

```json
{
  "pythonDllPath": "C:\\Python311\\python311.dll",
  "pythonPaths": [
    "C:\\Python311\\Lib\\site-packages",
    "C:\\MyCustomLibs"
  ],
  "autoDetectPythonPaths": true
}
```

#### 2. PythonConfigUpdated (Runner → Server)

```json
{
  "success": true,
  "message": "Configuration updated successfully",
  "appliedConfig": {
    "pythonDllPath": "C:\\Python311\\python311.dll",
    "pythonPaths": [
      "C:\\Python311\\Lib\\site-packages",
      "C:\\MyCustomLibs"
    ],
    "autoDetectPythonPaths": true
  }
}
```

#### 3. ValidatePythonPaths (Server → Runner)

```json
{
  "validationId": "guid-string",
  "paths": [
    "C:\\Python311\\Lib\\site-packages",
    "C:\\MyCustomLibs"
  ],
  "pythonDllPath": "C:\\Python311\\python311.dll"
}
```

#### 4. PythonPathsValidated (Runner → Server)

```json
{
  "validationId": "guid-string",
  "isValid": false,
  "results": [
    {
      "path": "C:\\Python311\\python311.dll",
      "exists": true,
      "type": "dll",
      "errorMessage": null
    },
    {
      "path": "C:\\Python311\\Lib\\site-packages",
      "exists": true,
      "type": "directory",
      "errorMessage": null
    },
    {
      "path": "C:\\MyCustomLibs",
      "exists": false,
      "type": "directory",
      "errorMessage": "Directory not found"
    }
  ]
}
```

### Runner SignalR Handler Implementation

**File**: `Automaton.Runner.Console/Services/HubService.cs`

```csharp
private void RegisterHandlers()
{
    // Existing handlers...
    
    // Python Configuration Handlers
    _hubConnection.On<RunnerPythonConfig>("UpdatePythonConfig", 
        async (config) => await HandleUpdatePythonConfig(config));
    
    _hubConnection.On<ValidatePythonPathsMessage>("ValidatePythonPaths",
        async (message) => await HandleValidatePythonPaths(message));
}

private async Task HandleUpdatePythonConfig(RunnerPythonConfig config)
{
    try
    {
        _logger.LogInformation("Received Python configuration update");
        
        // Validate paths locally
        var isValid = ValidatePathsLocally(config);
        
        if (!isValid)
        {
            _logger.LogWarning("Some Python paths are invalid on this machine");
        }
        
        // Save to local runner.json
        await fileStorageService.UpdatePythonConfigAsync(config);
        
        // Send confirmation back to server
        await _hubConnection.SendAsync("PythonConfigUpdated", new
        {
            Success = true,
            Message = "Configuration updated successfully",
            AppliedConfig = config
        });
        
        _logger.LogInformation("Python configuration updated successfully");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to update Python configuration");
        
        await _hubConnection.SendAsync("PythonConfigUpdated", new
        {
            Success = false,
            Message = $"Failed to update configuration: {ex.Message}"
        });
    }
}

private async Task HandleValidatePythonPaths(ValidatePythonPathsMessage message)
{
    try
    {
        _logger.LogInformation($"Validating Python paths (ValidationId: {message.ValidationId})");
        
        var results = new List<PathValidationResult>();
        
        // Validate Python DLL path
        if (!string.IsNullOrEmpty(message.PythonDllPath))
        {
            results.Add(ValidatePath(message.PythonDllPath, "dll"));
        }
        
        // Validate Python paths
        foreach (var path in message.Paths)
        {
            results.Add(ValidatePath(path, "directory"));
        }
        
        var response = new ValidatePythonPathsResponse
        {
            ValidationId = message.ValidationId,
            IsValid = results.All(r => r.Exists),
            Results = results
        };
        
        // Send results back to server
        await _hubConnection.SendAsync("PythonPathsValidated", response);
        
        _logger.LogInformation($"Validation complete: {results.Count(r => r.Exists)}/{results.Count} paths valid");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to validate Python paths");
    }
}

private PathValidationResult ValidatePath(string path, string type)
{
    bool exists = type == "dll" ? File.Exists(path) : Directory.Exists(path);
    
    return new PathValidationResult
    {
        Path = path,
        Exists = exists,
        Type = type,
        ErrorMessage = exists ? null : $"{type} not found on this machine"
    };
}

private bool ValidatePathsLocally(RunnerPythonConfig config)
{
    bool allValid = true;
    
    if (!string.IsNullOrEmpty(config.PythonDllPath))
    {
        allValid &= File.Exists(config.PythonDllPath);
    }
    
    foreach (var path in config.PythonPaths)
    {
        allValid &= Directory.Exists(path);
    }
    
    return allValid;
}
```

---

## UI Mockups and Component Structure

### 1. Runners Page - Configuration Dialog

**File**: `Automaton.Studio/Pages/Runners/RunnersPage.razor`

```razor
@page "/runners"
@using Automaton.Studio.Models
@using Automaton.Studio.Pages.Runners.Components

<!-- Existing runner list code... -->

<Table DataSource="@RunnersViewModel.Runners" Loading="@loading">
    <Column TData="RunnerModel" Title="Name" @bind-Field="@context.Name" />
    <Column TData="RunnerModel" Title="Status">
        <Tag Color="@(context.Status == RunnerStatus.Online ? "success" : "default")">
            @context.Status
        </Tag>
    </Column>
    <ActionColumn Title="Actions">
        <Space>
            <SpaceItem>
                <Button Size="@ButtonSize.Small" OnClick="@(() => ShowPythonConfigDialog(context))">
                    <Icon Type="setting" /> Configure Python
                </Button>
            </SpaceItem>
            <!-- Other actions... -->
        </Space>
    </ActionColumn>
</Table>

<!-- Python Configuration Modal -->
<PythonConfigDialog 
    @ref="pythonConfigDialog"
    Runner="@selectedRunner"
    OnConfigSaved="@HandleConfigSaved"
    OnValidationRequested="@HandleValidationRequested" />
```

### 2. Python Configuration Dialog Component

**File**: `Automaton.Studio/Pages/Runners/Components/PythonConfigDialog.razor`

```razor
@using Automaton.Studio.Models
@using Automaton.Studio.Server.Models

<Modal Title="@($"Python Configuration - {Runner?.Name}")"
       Visible="@visible"
       Width="800"
       OnOk="@SaveConfiguration"
       OnCancel="@Close"
       ConfirmLoading="@saving">
    
    <Form Model="@pythonConfig" LabelColSpan="6" WrapperColSpan="18">
        
        <!-- Auto-detect Section -->
        <FormItem Label="Auto-detect Python">
            <Checkbox @bind-Checked="@pythonConfig.AutoDetectPythonPaths">
                Automatically detect Python installation and paths
            </Checkbox>
            <Alert Type="@AlertType.Info" ShowIcon="true" Style="margin-top: 8px;">
                <MessageTemplate>
                    When enabled, the runner will attempt to automatically detect Python DLL and site-packages locations.
                    You can still provide additional paths below.
                </MessageTemplate>
            </Alert>
        </FormItem>
        
        <!-- Python DLL Path -->
        <FormItem Label="Python DLL Path">
            <Input @bind-Value="@pythonConfig.PythonDllPath" 
                   Placeholder="e.g., C:\Python311\python311.dll or /usr/lib/libpython3.11.so"
                   Style="width: 100%;" />
            <Text Type="@TextElementType.Secondary" Style="font-size: 12px;">
                Path to Python DLL/SO file. Leave empty to use auto-detection.
            </Text>
        </FormItem>
        
        <!-- Python Paths List -->
        <FormItem Label="Additional Python Paths">
            <Space Direction="SpaceDirection.Vertical" Style="width: 100%;">
                @foreach (var (path, index) in pythonConfig.PythonPaths.Select((p, i) => (p, i)))
                {
                    <SpaceItem Style="width: 100%;">
                        <Space Style="width: 100%;">
                            <SpaceItem Style="flex: 1;">
                                <Input @bind-Value="@pythonConfig.PythonPaths[index]" 
                                       Placeholder="e.g., C:\Python311\Lib\site-packages"
                                       Style="width: 100%;" />
                            </SpaceItem>
                            <SpaceItem>
                                <Button Danger 
                                        Size="@ButtonSize.Small"
                                        Icon="delete"
                                        OnClick="@(() => RemovePythonPath(index))">
                                    Remove
                                </Button>
                            </SpaceItem>
                        </Space>
                    </SpaceItem>
                }
                
                <SpaceItem>
                    <Button Type="@ButtonType.Dashed" 
                            Icon="plus"
                            OnClick="@AddPythonPath"
                            Block>
                        Add Python Path
                    </Button>
                </SpaceItem>
            </Space>
            <Text Type="@TextElementType.Secondary" Style="font-size: 12px; margin-top: 8px;">
                Additional directories for Python libraries and custom modules (e.g., site-packages, custom module locations).
            </Text>
        </FormItem>
        
        <!-- Validation Section -->
        <FormItem Label="Path Validation">
            <Space>
                <SpaceItem>
                    <Button OnClick="@ValidatePaths" 
                            Loading="@validating"
                            Icon="check-circle">
                        Validate Paths on Runner
                    </Button>
                </SpaceItem>
                @if (validationResults != null)
                {
                    <SpaceItem>
                        @if (validationResults.IsValid)
                        {
                            <Tag Color="success" Icon="check-circle">All paths valid</Tag>
                        }
                        else
                        {
                            <Tag Color="error" Icon="close-circle">
                                @validationResults.Results.Count(r => !r.Exists) invalid path(s)
                            </Tag>
                        }
                    </SpaceItem>
                }
            </Space>
            
            @if (validationResults?.Results != null && validationResults.Results.Any())
            {
                <div style="margin-top: 16px;">
                    <Text Strong>Validation Results:</Text>
                    <List DataSource="@validationResults.Results" Size="@ListSize.Small" Bordered>
                        <ChildContent Context="result">
                            <ListItem>
                                <Space>
                                    <SpaceItem>
                                        @if (result.Exists)
                                        {
                                            <Icon Type="check-circle" Theme="@IconThemeType.TwoTone" TwoToneColor="#52c41a" />
                                        }
                                        else
                                        {
                                            <Icon Type="close-circle" Theme="@IconThemeType.TwoTone" TwoToneColor="#ff4d4f" />
                                        }
                                    </SpaceItem>
                                    <SpaceItem Style="flex: 1;">
                                        <Text Code Style="word-break: break-all;">@result.Path</Text>
                                        @if (!string.IsNullOrEmpty(result.ErrorMessage))
                                        {
                                            <br />
                                            <Text Type="@TextElementType.Danger" Style="font-size: 12px;">
                                                @result.ErrorMessage
                                            </Text>
                                        }
                                    </SpaceItem>
                                    <SpaceItem>
                                        <Tag>@result.Type</Tag>
                                    </SpaceItem>
                                </Space>
                            </ListItem>
                        </ChildContent>
                    </List>
                </div>
            }
        </FormItem>
        
        <!-- Runner Status Warning -->
        @if (Runner?.Status != RunnerStatus.Online)
        {
            <FormItem>
                <Alert Type="@AlertType.Warning" ShowIcon="true">
                    <MessageTemplate>
                        <Text Strong>Runner is offline</Text>
                        <br />
                        <Text>Configuration will be saved to the database, but cannot be validated or synced to the runner until it comes online.</Text>
                    </MessageTemplate>
                </Alert>
            </FormItem>
        }
        
    </Form>
</Modal>

@code {
    [Parameter]
    public RunnerModel? Runner { get; set; }
    
    [Parameter]
    public EventCallback<RunnerPythonConfig> OnConfigSaved { get; set; }
    
    [Parameter]
    public EventCallback<ValidatePythonPathsRequest> OnValidationRequested { get; set; }
    
    private bool visible = false;
    private bool saving = false;
    private bool validating = false;
    private RunnerPythonConfig pythonConfig = new();
    private ValidatePythonPathsResponse? validationResults;
    
    public void Show()
    {
        visible = true;
        LoadConfiguration();
        StateHasChanged();
    }
    
    private void Close()
    {
        visible = false;
        validationResults = null;
    }
    
    private void LoadConfiguration()
    {
        if (Runner != null)
        {
            pythonConfig = new RunnerPythonConfig
            {
                PythonDllPath = Runner.PythonDllPath,
                PythonPaths = new List<string>(Runner.PythonPaths),
                AutoDetectPythonPaths = Runner.AutoDetectPythonPaths
            };
        }
    }
    
    private void AddPythonPath()
    {
        pythonConfig.PythonPaths.Add(string.Empty);
    }
    
    private void RemovePythonPath(int index)
    {
        pythonConfig.PythonPaths.RemoveAt(index);
    }
    
    private async Task ValidatePaths()
    {
        if (Runner == null) return;
        
        validating = true;
        try
        {
            var request = new ValidatePythonPathsRequest
            {
                RunnerId = Runner.Id,
                Paths = pythonConfig.PythonPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
                PythonDllPath = pythonConfig.PythonDllPath
            };
            
            await OnValidationRequested.InvokeAsync(request);
        }
        finally
        {
            validating = false;
        }
    }
    
    private async Task SaveConfiguration()
    {
        if (Runner == null) return;
        
        saving = true;
        try
        {
            // Remove empty paths
            pythonConfig.PythonPaths = pythonConfig.PythonPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            
            await OnConfigSaved.InvokeAsync(pythonConfig);
            Close();
        }
        finally
        {
            saving = false;
        }
    }
    
    public void SetValidationResults(ValidatePythonPathsResponse results)
    {
        validationResults = results;
        StateHasChanged();
    }
}
```

### 3. Settings Page - Local Python Configuration

**File**: `Automaton.Studio/Pages/Settings/SettingsPage.razor`

```razor
@page "/settings"

<!-- Existing settings sections... -->

<!-- Python Configuration Section -->
<div class="section">
    <Card Title="Python Configuration (Local Execution)" 
          Class="settings-card" 
          Bordered="true">
        <Extra>
            <Icon Type="code" Style="color: #1890ff;" />
        </Extra>
        <ChildContent>
            <Space Direction="SpaceDirection.Vertical" Style="width: 100%;">
                <SpaceItem>
                    <Paragraph>
                        Configure Python paths for executing flows and custom steps locally 
                        from this Studio instance (not on runners). These settings apply only 
                        to your browser session.
                    </Paragraph>
                </SpaceItem>
                
                <SpaceItem>
                    <Alert Type="@AlertType.Info" ShowIcon="true">
                        <MessageTemplate>
                            <Text>
                                This configuration is separate from runner configurations. 
                                Use this for local development and testing before deploying to runners.
                            </Text>
                        </MessageTemplate>
                    </Alert>
                </SpaceItem>
                
                <SpaceItem>
                    <Form Model="@localPythonConfig" Layout="@FormLayout.Vertical">
                        <FormItem Label="Auto-detect Python Paths">
                            <Checkbox @bind-Checked="@localPythonConfig.AutoDetectPythonPaths">
                                Automatically detect Python installation
                            </Checkbox>
                        </FormItem>
                        
                        <FormItem Label="Python DLL Path">
                            <Input @bind-Value="@localPythonConfig.PythonDllPath" 
                                   Placeholder="e.g., C:\Python311\python311.dll" />
                        </FormItem>
                        
                        <FormItem Label="Additional Python Paths">
                            @foreach (var (path, index) in localPythonConfig.PythonPaths.Select((p, i) => (p, i)))
                            {
                                <Space Style="width: 100%; margin-bottom: 8px;">
                                    <SpaceItem Style="flex: 1;">
                                        <Input @bind-Value="@localPythonConfig.PythonPaths[index]" />
                                    </SpaceItem>
                                    <SpaceItem>
                                        <Button Danger Size="@ButtonSize.Small" 
                                                OnClick="@(() => localPythonConfig.PythonPaths.RemoveAt(index))">
                                            <Icon Type="delete" />
                                        </Button>
                                    </SpaceItem>
                                </Space>
                            }
                            <Button Type="@ButtonType.Dashed" 
                                    OnClick="@(() => localPythonConfig.PythonPaths.Add(string.Empty))">
                                <Icon Type="plus" /> Add Path
                            </Button>
                        </FormItem>
                        
                        <FormItem>
                            <Button Type="@ButtonType.Primary" OnClick="@SaveLocalPythonConfig">
                                Save Configuration
                            </Button>
                        </FormItem>
                    </Form>
                </SpaceItem>
            </Space>
        </ChildContent>
    </Card>
</div>
```


---

## Database Migration Strategy

### Migration Steps

#### 1. Create Migration

```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add AddPythonConfigToRunner
```

#### 2. Migration File

**File**: `Automaton.Studio.Server/Migrations/YYYYMMDDHHMMSS_AddPythonConfigToRunner.cs`

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    public partial class AddPythonConfigToRunner : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PythonDllPath",
                table: "Runners",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PythonPathsJson",
                table: "Runners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoDetectPythonPaths",
                table: "Runners",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PythonDllPath",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "PythonPathsJson",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "AutoDetectPythonPaths",
                table: "Runners");
        }
    }
}
```

#### 3. Update DbContext

**File**: `Automaton.Studio.Server/Data/ApplicationDbContext.cs`

```csharp
modelBuilder.Entity<Runner>(entity =>
{
    entity.HasKey(e => new { e.Id });
    
    // Python configuration properties
    entity.Property(e => e.PythonDllPath)
        .HasMaxLength(500)
        .IsRequired(false);
    
    entity.Property(e => e.PythonPathsJson)
        .HasColumnType("text")
        .IsRequired(false);
    
    entity.Property(e => e.AutoDetectPythonPaths)
        .IsRequired()
        .HasDefaultValue(true);
});
```

#### 4. Apply Migration

```bash
# Development
dotnet ef database update --project src/Automaton.Studio.Server

# Production
dotnet ef database update --connection "YourConnectionString" --project src/Automaton.Studio.Server
```

### Backward Compatibility

- All new columns are **nullable** (except AutoDetectPythonPaths with default value)
- Existing runners continue working with auto-detection enabled by default
- No data migration needed
- Runner console gracefully handles missing Python config in runner.json

### Data Validation

```csharp
// Server-side validation
public class RunnerPythonConfigValidator : AbstractValidator<RunnerPythonConfig>
{
    public RunnerPythonConfigValidator()
    {
        RuleFor(x => x.PythonDllPath)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.PythonDllPath))
            .WithMessage("Python DLL path must not exceed 500 characters");
        
        RuleForEach(x => x.PythonPaths)
            .MaximumLength(500)
            .WithMessage("Each Python path must not exceed 500 characters");
        
        RuleFor(x => x.PythonPaths)
            .Must(paths => paths.Count <= 20)
            .WithMessage("Maximum 20 Python paths allowed");
    }
}
```

---

## Security Considerations

### 1. Access Control

```csharp
// Only runner owner can view/modify Python configuration
[Authorize]
[HttpPut("{id}/python-config")]
public async Task<IActionResult> UpdatePythonConfig(Guid id, ...)
{
    var userId = GetUserId();
    var runner = await runnerService.GetRunnerByIdAsync(id, userId);
    
    if (runner == null)
        return NotFound(); // or Forbidden if runner exists but user doesn't own it
    
    // ... update logic
}
```

### 2. Path Validation and Sanitization

```csharp
public class PathSanitizer
{
    private static readonly char[] InvalidChars = Path.GetInvalidPathChars();
    
    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        
        // Remove any invalid characters
        var sanitized = new string(path.Where(c => !InvalidChars.Contains(c)).ToArray());
        
        // Prevent path traversal attempts
        sanitized = sanitized.Replace("..", string.Empty);
        
        // Normalize path separators
        sanitized = Path.GetFullPath(sanitized);
        
        return sanitized;
    }
    
    public static bool IsValidPath(string path)
    {
        try
        {
            Path.GetFullPath(path);
            return !path.Contains("..");
        }
        catch
        {
            return false;
        }
    }
}
```

### 3. Injection Prevention

```csharp
// Never execute paths as commands
// Only use for File.Exists() and Directory.Exists() checks
public class SecurePathValidator
{
    public PathValidationResult ValidatePath(string path, string type)
    {
        // Sanitize first
        if (!PathSanitizer.IsValidPath(path))
        {
            return new PathValidationResult
            {
                Path = path,
                Exists = false,
                ErrorMessage = "Invalid path format"
            };
        }
        
        // Only check existence, never execute
        bool exists = type == "dll" 
            ? File.Exists(path) 
            : Directory.Exists(path);
        
        return new PathValidationResult
        {
            Path = path,
            Exists = exists,
            Type = type,
            ErrorMessage = exists ? null : $"{type} not found"
        };
    }
}
```

### 4. Audit Logging

```csharp
public class PythonConfigAuditService
{
    private readonly ILogger<PythonConfigAuditService> logger;
    
    public void LogConfigurationChange(Guid runnerId, string runnerName, 
        Guid userId, string userName, RunnerPythonConfig oldConfig, 
        RunnerPythonConfig newConfig)
    {
        logger.LogInformation(
            "Python configuration changed for runner {RunnerId} ({RunnerName}) " +
            "by user {UserId} ({UserName}). " +
            "Old: DllPath={OldDll}, Paths={OldPaths}, AutoDetect={OldAutoDetect}. " +
            "New: DllPath={NewDll}, Paths={NewPaths}, AutoDetect={NewAutoDetect}",
            runnerId, runnerName, userId, userName,
            oldConfig.PythonDllPath, string.Join(";", oldConfig.PythonPaths), oldConfig.AutoDetectPythonPaths,
            newConfig.PythonDllPath, string.Join(";", newConfig.PythonPaths), newConfig.AutoDetectPythonPaths
        );
    }
}
```

### 5. Rate Limiting

```csharp
// Prevent excessive validation requests
[RateLimit(MaxRequests = 10, WindowSeconds = 60)]
[HttpPost("{id}/validate-python-paths")]
public async Task<IActionResult> ValidatePythonPaths(...)
{
    // ... validation logic
}
```

### 6. Sensitive Data Protection

```csharp
// Don't log full paths in production
public class PythonConfigDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PythonDllPath { get; set; }
    
    // Mask paths in logs
    public string GetMaskedDllPath()
    {
        if (string.IsNullOrEmpty(PythonDllPath))
            return "Not configured";
        
        var fileName = Path.GetFileName(PythonDllPath);
        return $"***/{fileName}";
    }
}
```

---

## Testing Strategy

### 1. Unit Tests

#### FileStorageService Tests

**File**: `UnitTests/Automaton.Runner.Console.Tests/Services/FileStorageServiceTests.cs`

```csharp
public class FileStorageServiceTests
{
    [Fact]
    public async Task SavePythonConfig_ShouldPersistToFile()
    {
        // Arrange
        var service = new FileStorageService();
        var config = new RunnerPythonConfig
        {
            PythonDllPath = "C:\\Python311\\python311.dll",
            PythonPaths = new List<string> { "C:\\Python311\\Lib\\site-packages" },
            AutoDetectPythonPaths = true
        };
        
        // Act
        await service.UpdatePythonConfigAsync(config);
        var loaded = service.GetPythonConfig();
        
        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(config.PythonDllPath, loaded.PythonDllPath);
        Assert.Equal(config.PythonPaths.Count, loaded.PythonPaths.Count);
        Assert.Equal(config.AutoDetectPythonPaths, loaded.AutoDetectPythonPaths);
    }
    
    [Fact]
    public void GetPythonConfig_WhenNotConfigured_ShouldReturnDefaults()
    {
        // Arrange
        var service = new FileStorageService();
        
        // Act
        var config = service.GetPythonConfig();
        
        // Assert
        Assert.NotNull(config);
        Assert.True(config.AutoDetectPythonPaths);
        Assert.Empty(config.PythonPaths);
    }
}
```

#### ConfigurationService Tests

```csharp
public class ConfigurationServiceTests
{
    [Fact]
    public void GetPythonConfig_ShouldRespectPriority()
    {
        // Priority: Local file → Env vars → CLI → Auto-detection
        
        // Test with environment variable
        Environment.SetEnvironmentVariable("AUTOMATON_PYTHON_DLL", "C:\\Python311\\python311.dll");
        
        var service = new ConfigurationService(...);
        var config = service.GetPythonConfig();
        
        Assert.Equal("C:\\Python311\\python311.dll", config.PythonDllPath);
    }
}
```

#### Path Validation Tests

```csharp
public class PathValidationTests
{
    [Theory]
    [InlineData("C:\\Python311\\python311.dll", true)]
    [InlineData("C:\\Invalid\\Path\\python.dll", false)]
    [InlineData("../../../etc/passwd", false)] // Path traversal attempt
    public void ValidatePath_ShouldDetectValidAndInvalidPaths(string path, bool expectedValid)
    {
        var validator = new SecurePathValidator();
        var result = validator.ValidatePath(path, "dll");
        
        Assert.Equal(expectedValid, result.Exists);
    }
}
```

### 2. Integration Tests

#### API Endpoint Tests

**File**: `UnitTests/Automaton.Studio.Server.Tests/Controllers/RunnersControllerTests.cs`

```csharp
public class RunnersControllerPythonConfigTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetPythonConfig_ShouldReturnConfiguration()
    {
        // Arrange
        var client = _factory.CreateClient();
        var runnerId = await CreateTestRunner();
        
        // Act
        var response = await client.GetAsync($"/api/runners/{runnerId}/python-config");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var config = await response.Content.ReadFromJsonAsync<RunnerPythonConfig>();
        Assert.NotNull(config);
    }
    
    [Fact]
    public async Task UpdatePythonConfig_ShouldPersistChanges()
    {
        // Arrange
        var client = _factory.CreateClient();
        var runnerId = await CreateTestRunner();
        var config = new RunnerPythonConfig
        {
            PythonDllPath = "C:\\Python311\\python311.dll",
            PythonPaths = new List<string> { "C:\\Test" },
            AutoDetectPythonPaths = false
        };
        
        // Act
        var response = await client.PutAsJsonAsync($"/api/runners/{runnerId}/python-config", config);
        
        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify persistence
        var getResponse = await client.GetAsync($"/api/runners/{runnerId}/python-config");
        var savedConfig = await getResponse.Content.ReadFromJsonAsync<RunnerPythonConfig>();
        Assert.Equal(config.PythonDllPath, savedConfig.PythonDllPath);
    }
}
```

#### SignalR Communication Tests

```csharp
public class SignalRPythonConfigTests
{
    [Fact]
    public async Task UpdatePythonConfig_ShouldTriggerSignalRMessage()
    {
        // Arrange
        var hubConnection = await CreateTestHubConnection();
        RunnerPythonConfig receivedConfig = null;
        
        hubConnection.On<RunnerPythonConfig>("UpdatePythonConfig", config =>
        {
            receivedConfig = config;
        });
        
        // Act
        await UpdateRunnerPythonConfig(testRunnerId, new RunnerPythonConfig { ... });
        await Task.Delay(1000); // Wait for SignalR message
        
        // Assert
        Assert.NotNull(receivedConfig);
    }
}
```

### 3. End-to-End Tests

```csharp
public class PythonConfigE2ETests
{
    [Fact]
    public async Task FullConfigurationFlow_ShouldWork()
    {
        // 1. Start runner
        var runner = await StartTestRunner();
        
        // 2. Configure Python via API
        var config = new RunnerPythonConfig
        {
            PythonDllPath = "C:\\Python311\\python311.dll",
            PythonPaths = new List<string> { "C:\\Test" },
            AutoDetectPythonPaths = false
        };
        await UpdatePythonConfig(runner.Id, config);
        
        // 3. Wait for runner to receive and apply config
        await Task.Delay(2000);
        
        // 4. Verify runner has applied config
        var runnerConfig = runner.FileStorage.GetPythonConfig();
        Assert.Equal(config.PythonDllPath, runnerConfig.PythonDllPath);
        
        // 5. Execute Python script to verify configuration works
        var result = await ExecutePythonScript(runner.Id, "import sys; print(sys.path)");
        Assert.Contains("C:\\Test", result.Output);
    }
}
```

### 4. Manual Testing Checklist

#### UI Testing
- [ ] Open Runners page and see list of runners
- [ ] Click "Configure Python" button on a runner
- [ ] Modal opens with current configuration
- [ ] Toggle auto-detect checkbox
- [ ] Enter Python DLL path
- [ ] Add multiple Python paths
- [ ] Remove Python paths
- [ ] Click "Validate Paths on Runner" (runner must be online)
- [ ] See validation results with green checkmarks and red X marks
- [ ] Save configuration
- [ ] Verify configuration persists after page reload

#### CLI Testing
- [ ] Run `automaton-runner config python` command
- [ ] Interactive prompts appear for configuration
- [ ] Configuration saved to runner.json
- [ ] Run `automaton-runner status` to see Python config
- [ ] Use `--dll` and `--add-path` flags
- [ ] Verify environment variables override file config

#### Cross-Platform Testing
- [ ] Test on Windows with paths like `C:\Python311\python311.dll`
- [ ] Test on Linux with paths like `/usr/lib/libpython3.11.so`
- [ ] Test on macOS with paths like `/usr/local/lib/libpython3.11.dylib`
- [ ] Verify path separators are handled correctly

---

## Documentation Requirements

### 1. User Guide

**File**: `docs/user-guide/python-configuration.md`

Topics to cover:
- Overview of Python configuration
- Why Python paths need to be configured
- How to configure Python paths via UI
- How to configure Python paths via CLI
- Understanding auto-detection
- Troubleshooting common issues
- Platform-specific examples (Windows, Linux, macOS)

### 2. Administrator Guide

**File**: `docs/admin-guide/python-configuration.md`

Topics to cover:
- Database schema changes
- Migration procedures
- Security considerations
- Monitoring and auditing
- Performance implications
- Backup and disaster recovery

### 3. API Documentation

**File**: `docs/api/runners-python-config.md`

Document all API endpoints:
- `GET /api/runners/{id}/python-config`
- `PUT /api/runners/{id}/python-config`
- `POST /api/runners/{id}/validate-python-paths`

Include request/response examples for each endpoint.

### 4. CLI Reference

**File**: `docs/cli/config-python.md`

Document CLI commands:
- `automaton-runner config python` - Interactive configuration
- `automaton-runner config python --dll <path>` - Set Python DLL
- `automaton-runner config python --add-path <path>` - Add Python path
- `automaton-runner config python --auto-detect` - Enable auto-detection
- `automaton-runner config python --show` - Display current configuration

### 5. Developer Guide

**File**: `docs/developer-guide/python-configuration-architecture.md`

Topics to cover:
- Architecture overview
- Data flow diagrams
- Extension points for custom Python engines
- How to add new configuration options
- Testing guidelines

---

## Implementation Phases

### Phase 1: Data Model Extensions (Week 1)

**Tasks**:
- [ ] Extend Runner entity with Python config properties
- [ ] Create all DTOs (RunnerPythonConfig, requests, responses)
- [ ] Extend RunnerModel for UI
- [ ] Create RunnerPythonConfig model for console
- [ ] Write unit tests for models

**Deliverables**:
- All model classes created
- Unit tests passing

### Phase 2: Runner-side Configuration Storage (Week 1-2)

**Tasks**:
- [ ] Extend FileStorageService with Python config methods
- [ ] Update ConfigurationService to load Python config
- [ ] Implement configuration priority logic
- [ ] Add environment variable support
- [ ] Write unit tests for FileStorageService

**Deliverables**:
- FileStorageService can save/load Python config
- Configuration priority working correctly
- Unit tests passing

### Phase 3: Server-side API Endpoints (Week 2)

**Tasks**:
- [ ] Extend RunnersController with Python config endpoints
- [ ] Implement GetPythonConfig method
- [ ] Implement UpdatePythonConfig method
- [ ] Implement ValidatePythonPaths method
- [ ] Add authorization checks
- [ ] Add validation and error handling
- [ ] Write integration tests

**Deliverables**:
- All API endpoints working
- Integration tests passing

### Phase 4: Runner SignalR Handlers (Week 2-3)

**Tasks**:
- [ ] Add SignalR hub methods
- [ ] Implement UpdatePythonConfig handler in HubService
- [ ] Implement ValidatePythonPaths handler
- [ ] Add path validation logic
- [ ] Implement two-way communication
- [ ] Write SignalR integration tests

**Deliverables**:
- SignalR communication working
- Path validation functional
- Integration tests passing

### Phase 5: UI Implementation (Week 3-4)

**Tasks**:
- [ ] Create PythonConfigDialog component
- [ ] Integrate dialog into RunnersPage
- [ ] Implement path validation UI
- [ ] Add Settings page Python config section
- [ ] Implement LocalStorage for Studio config
- [ ] Add loading states and error handling
- [ ] Write UI component tests

**Deliverables**:
- Fully functional UI for Python configuration
- Settings page completed
- UI tests passing

### Phase 6: Database Migration (Week 4)

**Tasks**:
- [ ] Create EF Core migration
- [ ] Update DbContext configuration
- [ ] Test migration on dev database
- [ ] Create rollback procedure
- [ ] Document migration steps

**Deliverables**:
- Migration scripts created and tested
- Documentation completed

### Phase 7: Configuration Usage Integration (Week 4-5)

**Tasks**:
- [ ] Update runner startup to use Python config
- [ ] Integrate with ScriptEngineFactory
- [ ] Add CLI commands for Python configuration
- [ ] Update runner registration workflow
- [ ] End-to-end testing

**Deliverables**:
- Python config fully integrated
- CLI commands working
- E2E tests passing

### Phase 8: Testing and Documentation (Week 5-6)

**Tasks**:
- [ ] Complete unit test suite
- [ ] Complete integration test suite
- [ ] Perform manual testing on all platforms
- [ ] Write user documentation
- [ ] Write admin documentation
- [ ] Write API documentation
- [ ] Write CLI reference
- [ ] Create troubleshooting guide

**Deliverables**:
- All tests passing
- Complete documentation

---

## Pros and Cons Analysis

### Approach 1: UI-Managed (Database Storage Only)

**Pros**:
- ✅ Centralized management - all configuration in one place
- ✅ Easy to view all runners' configurations
- ✅ No runner CLI complexity
- ✅ Consistent backup with database backups
- ✅ Simple implementation (no file sync needed)

**Cons**:
- ❌ Can't validate paths until runtime
- ❌ Runner might override with local settings
- ❌ Requires network for configuration
- ❌ Less flexible for edge cases (CI/CD, automation)
- ❌ No offline configuration capability

### Approach 2: Runner-Managed (Local File Storage Only)

**Pros**:
- ✅ Runner validates paths locally
- ✅ More flexible (environment variables, command-line)
- ✅ Works offline after initial setup
- ✅ Runner has full control
- ✅ Better for automation and CI/CD
- ✅ No database dependency

**Cons**:
- ❌ Harder to manage from UI
- ❌ Must implement two-way sync if UI needed
- ❌ More complex setup during registration
- ❌ Can get out of sync with server expectations
- ❌ Difficult to view all runner configurations centrally

### Approach 3: Hybrid (Recommended)

**Pros**:
- ✅ Best of both worlds
- ✅ UI provides convenience for non-technical users
- ✅ CLI supports technical users and automation
- ✅ Runner has final authority on its configuration
- ✅ Validates paths on actual machine before use
- ✅ Centralized management through UI
- ✅ Fallback to auto-detection ensures it works out-of-the-box
- ✅ Flexible configuration sources (file, env vars, CLI, UI)
- ✅ Clean separation of concerns

**Cons**:
- ❌ More implementation work (two-way sync)
- ❌ Complexity in synchronization logic
- ❌ Potential for sync conflicts
- ❌ More testing required
- ❌ More documentation needed

### Decision Matrix

| Criteria | UI-Only | Runner-Only | Hybrid |
|----------|---------|-------------|--------|
| Ease of Implementation | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ |
| User Experience | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| Flexibility | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Path Validation | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Automation Support | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Centralized Management | ⭐⭐⭐⭐⭐ | ⭐ | ⭐⭐⭐⭐⭐ |
| Offline Capability | ⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Total** | **19** | **26** | **32** |

---

## Conclusion

### Recommendation: Hybrid Approach

The **hybrid configuration strategy** is the recommended solution because it:

1. **Maximizes Flexibility**: Supports both UI-driven and CLI-driven workflows
2. **Ensures Validation**: Paths are validated on the actual runner machine where they'll be used
3. **Improves User Experience**: Non-technical users can use the UI, technical users can use CLI
4. **Maintains Authority**: Runner has final say on its configuration, preventing invalid configurations
5. **Enables Automation**: Environment variables and CLI support make CI/CD integration seamless
6. **Provides Centralized Visibility**: All runner configurations can be viewed and managed from the UI
7. **Works Offline**: Runners can operate with locally-stored configuration
8. **Includes Safety Net**: Auto-detection fallback ensures basic functionality even without configuration

### Key Success Factors

1. **Clear Documentation**: Comprehensive guides for both UI and CLI usage
2. **Robust Testing**: Thorough test coverage including cross-platform tests
3. **Security First**: Proper validation, sanitization, and access control
4. **Gradual Rollout**: Backward-compatible migration allows gradual adoption
5. **Monitoring**: Audit logging for configuration changes
6. **Support**: Clear troubleshooting guides for common issues

### Next Steps

1. **Review and Approval**: Stakeholder review of this design document
2. **Implementation**: Follow the phased implementation plan (8 phases over 6 weeks)
3. **Testing**: Comprehensive testing on all supported platforms
4. **Documentation**: Complete all user-facing and developer documentation
5. **Release**: Deploy with backward-compatible migration
6. **Support**: Monitor usage and gather feedback for improvements

---

## Appendix

### A. Sample runner.json with Python Configuration

```json
{
  "RunnerId": "550e8400-e29b-41d4-a716-446655440000",
  "RunnerName": "Production-Runner-01",
  "RegisteredAt": "2024-01-15T10:30:00Z",
  "PythonConfig": {
    "PythonDllPath": "C:\\Python311\\python311.dll",
    "PythonPaths": [
      "C:\\Python311\\Lib\\site-packages",
      "C:\\CustomLibs\\MyPythonModules"
    ],
    "AutoDetectPythonPaths": true
  }
}
```

### B. Environment Variables Reference

```bash
# Python DLL path
export AUTOMATON_PYTHON_DLL="/usr/lib/libpython3.11.so"

# Python paths (colon-separated on Linux/Mac, semicolon on Windows)
export AUTOMATON_PYTHON_PATHS="/usr/local/lib/python3.11/site-packages:/home/user/custom_modules"

# Auto-detection flag
export AUTOMATON_PYTHON_AUTO_DETECT="true"
```

### C. CLI Examples

```bash
# Interactive configuration
automaton-runner config python

# Set Python DLL
automaton-runner config python --dll "C:\Python311\python311.dll"

# Add Python path
automaton-runner config python --add-path "C:\CustomLibs"

# Enable auto-detection
automaton-runner config python --auto-detect

# Disable auto-detection
automaton-runner config python --no-auto-detect

# Show current configuration
automaton-runner config python --show

# Reset to defaults
automaton-runner config python --reset
```

### D. Troubleshooting Common Issues

**Issue**: Runner can't find Python DLL

**Solution**:
1. Verify Python is installed: `python --version`
2. Find Python DLL location: `python -c "import sys; print(sys.executable)"`
3. Configure in runner: `automaton-runner config python --dll <path>`

**Issue**: Python imports fail even with configured paths

**Solution**:
1. Check if paths exist: `automaton-runner config python --show`
2. Validate paths: Use validation feature in UI or CLI
3. Verify path permissions: Ensure runner process can read the directories

**Issue**: Configuration not syncing between UI and runner

**Solution**:
1. Check runner is online: Verify status in Runners page
2. Check SignalR connection: Review runner logs for connection errors
3. Manually trigger sync: Update configuration again from UI

---

**Document Version**: 1.0  
**Last Updated**: 2025-10-15  
**Author**: Automaton Development Team  
**Status**: Ready for Implementation

