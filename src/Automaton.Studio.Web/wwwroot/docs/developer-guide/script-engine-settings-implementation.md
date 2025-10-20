# Script Engine Settings - Final Implementation Guide

**Version**: 2.0 (Final Implementation)  
**Date**: October 2025
**Status**: ✅ Implemented

---

## Overview

This document describes the **final implemented approach** for loading and applying Python script engine settings in both Runner and Desktop environments.

---

## Architecture Summary

### Key Design Decisions

1. **Singleton Configuration**: `ScriptEngineConfiguration` registered as **Singleton** so settings persist for application lifetime
2. **IHostedService Loading**: Settings loaded at startup via `IHostedService` to avoid async/sync deadlocks
3. **Runtime Reload**: Desktop supports immediate updates without restart; Runner requires restart (or optional SignalR)
4. **No Deadlocks**: All async database calls happen after DI container is fully built

---

## Configuration Flow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Application Starts                                        │
│    → DI Container Built                                      │
│    → ScriptEngineConfiguration registered as SINGLETON      │
│    → appsettings.json loaded (base config)                  │
└──────────────────┬──────────────────────────────────────────┘
                   ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. IHostedService.StartAsync() Runs                         │
│    → ScriptEngineSettingsInitializer (Runner)               │
│    → DesktopScriptEngineSettingsInitializer (Desktop)       │
│    → Gets SINGLETON ScriptEngineConfiguration               │
│    → Loads settings from database (async)                   │
│    → Calls config.LoadFromXXXSettings(settings)             │
│    → Settings persist in singleton ✅                        │
└──────────────────┬──────────────────────────────────────────┘
                   ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. Script Execution                                          │
│    → DI resolves ScriptEngineFactory (scoped)               │
│    → Gets SINGLETON ScriptEngineConfiguration               │
│    → Creates PythonNetScriptEngine(config)                  │
│    → Engine uses pre-loaded settings ✅                      │
└─────────────────────────────────────────────────────────────┘
```

---

## Implementation Details

### 1. Service Registration

**Location**: `src/Automaton.Core.Scripting/ServiceCollectionExtensions.cs`

```csharp
public static void AddScriptingForRunner(this IServiceCollection services)
{
    // SINGLETON - Settings persist for app lifetime
    services.AddSingleton<ScriptEngineConfiguration>(provider =>
    {
        var configuration = provider.GetService<IConfiguration>();
        var config = new ScriptEngineConfiguration
        {
            UsePythonNet = true,
            EnableDebugMode = false
        };
        configuration?.GetSection("Scripting").Bind(config);
        return config;
    });
    
    services.AddScoped<ScriptEngineFactory>();
    services.AddScoped<ScriptEngineHost>();
}

public static void AddScriptingForDesktop(this IServiceCollection services)
{
    // SINGLETON - Settings persist for app lifetime
    services.AddSingleton<ScriptEngineConfiguration>(provider =>
    {
        var configuration = provider.GetService<IConfiguration>();
        var config = new ScriptEngineConfiguration
        {
            UsePythonNet = true,
            EnableDebugMode = true
        };
        configuration?.GetSection("Scripting").Bind(config);
        return config;
    });
    
    services.AddScoped<ScriptEngineFactory>();
    services.AddScoped<ScriptEngineHost>();
}
```

### 2. Settings Initializers (IHostedService)

**Desktop**: `src/Automaton.Studio/Services/DesktopScriptEngineSettingsInitializer.cs`

```csharp
public class DesktopScriptEngineSettingsInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ScriptEngineConfiguration _config; // Injected singleton
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadSettingsAsync();
    }
    
    public async Task ReloadSettingsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetService<DesktopSettingsService>();
        
        settingsService?.ClearCache();
        var settings = await settingsService.GetPythonSettingsAsync();
        
        if (settings != null)
        {
            _config.LoadFromDesktopSettings(settings); // Updates singleton!
        }
    }
}
```

**Runner**: `src/Automaton.Runner.Console/Services/ScriptEngineSettingsInitializer.cs`

```csharp
public class ScriptEngineSettingsInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ScriptEngineConfiguration _config; // Injected singleton
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadSettingsAsync();
    }
    
    public async Task ReloadSettingsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetService<RunnerSettingsService>();
        
        settingsService?.ClearCache();
        var settings = await settingsService.LoadSettingsAsync();
        
        if (settings != null)
        {
            _config.LoadFromRunnerSettings(settings); // Updates singleton!
        }
    }
}
```

### 3. Registration

**Runner**: `src/Automaton.Runner.Console/Program.cs`

```csharp
services.AddScoped<RunnerSettingsService>();
services.AddHostedService<ScriptEngineSettingsInitializer>();
services.AddScriptingForRunner();
```

**Desktop**: `src/Automaton.Studio/Extensions/ServiceCollectionExtensions.cs`

```csharp
services.AddScoped<DesktopSettingsService>();
services.AddHostedService<DesktopScriptEngineSettingsInitializer>();
services.AddScriptingForDesktop();
```

### 4. Runtime Reload (Desktop Only)

**Location**: `src/Automaton.Studio/Pages/Settings/SettingsPage.razor`

```csharp
@inject DesktopSettingsService DesktopSettingsService
@inject DesktopScriptEngineSettingsInitializer SettingsInitializer

private async Task SavePythonSettings()
{
    var settings = new PythonEngineSettings
    {
        PythonDllPath = pythonDllPath,
        PythonPaths = pythonPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
    };

    var success = await DesktopSettingsService.SavePythonSettingsAsync(settings);
    
    if (success)
    {
        // Reload settings into singleton immediately!
        await SettingsInitializer.ReloadSettingsAsync();
        
        MessageService.Success("Settings saved and applied! No restart needed.");
    }
}
```

---

## Configuration Priority

```
1. Hard-coded defaults (in service registration)
   ↓
2. appsettings.json "Scripting" section (optional)
   ↓
3. Database settings (loaded at startup)
   ↓
4. Runtime updates (Desktop only, via ReloadSettingsAsync)
```

---

## Runtime Behavior

### Desktop (Automaton Studio)
✅ **Immediate updates - No restart required**

1. User edits settings in UI → Saves
2. Settings saved to database
3. `SettingsInitializer.ReloadSettingsAsync()` called
4. Singleton configuration updated in place
5. Next script execution uses new settings

### Runner (Automaton.Runner.Console)
⚠️ **Restart required** (or optional SignalR implementation)

1. User edits runner settings in UI → Saves
2. Settings saved to database
3. Runner must be restarted
4. On restart, `ScriptEngineSettingsInitializer` loads new settings

---

## Why This Approach?

### ✅ Advantages

1. **No Deadlocks**: Async loading happens after DI container is built
2. **Performance**: Settings loaded once at startup, cached in singleton
3. **Simplicity**: Standard .NET pattern (IHostedService)
4. **Flexibility**: Desktop gets immediate updates, Runner can optionally add SignalR
5. **Testability**: Clean separation of concerns, easy to mock

### ⚠️ Considerations

1. **Runner Restart**: Requires restart for settings changes (acceptable for long-running services)
2. **Singleton Scope**: Settings shared across all requests (intended behavior)
3. **Thread Safety**: LoadFromXXXSettings methods not thread-safe (acceptable - called sequentially)

---

## Testing

### Unit Tests
- Singleton lifetime verification
- Settings loading methods
- Priority logic

### Integration Tests
- Startup loading
- Runtime reload (Desktop)
- Script execution with custom settings

### Manual Tests
- Desktop immediate updates
- Runner restart behavior
- Settings persistence

---

## Future Enhancements

### Option 1: SignalR Live Reload for Runners

```csharp
// After saving runner settings:
await _automatonHub.Clients.Client(runner.ConnectionId)
    .SendAsync("ReloadScriptEngineSettings");

// In Runner:
connection.On("ReloadScriptEngineSettings", async () => {
    await _settingsInitializer.ReloadSettingsAsync();
});
```

### Option 2: Settings Validation

Add validation before saving to prevent invalid paths from breaking execution.

### Option 3: Test Configuration Button

UI button to test if Python DLL path and packages are accessible before saving.

---

## Related Files

- `src/Automaton.Core.Scripting/ServiceCollectionExtensions.cs` - Registration
- `src/Automaton.Studio/Services/DesktopScriptEngineSettingsInitializer.cs` - Desktop loader
- `src/Automaton.Runner.Console/Services/ScriptEngineSettingsInitializer.cs` - Runner loader
- `src/Automaton.Studio/Pages/Settings/SettingsPage.razor` - Desktop UI with reload
- `src/Automaton.Studio/Pages/Runners/RunnerEditPage.razor` - Runner settings UI

---

## Glossary

- **Singleton**: One instance for entire application lifetime
- **Scoped**: One instance per request/scope
- **IHostedService**: Background service that runs during application startup/shutdown
- **Deadlock**: When async code blocks on sync context (avoided by using IHostedService)

---

**Status**: ✅ Fully Implemented  
**Last Updated**: October 2025
