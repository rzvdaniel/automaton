using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Core.Scripting;

public static class ServiceCollectionExtensions
{
    public static void AddScripting(this IServiceCollection services)
    {
        services.AddScoped<ScriptEngineConfiguration>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var config = new ScriptEngineConfiguration();
            configuration?.GetSection("Scripting").Bind(config);
            return config;
        });
        
        services.AddScoped<ScriptEngineHost>();
    }

    public static void AddScripting(this IServiceCollection services, Action<ScriptEngineConfiguration> configureOptions)
    {
        services.AddScoped<ScriptEngineConfiguration>(provider =>
        {
            var config = new ScriptEngineConfiguration();
            configureOptions(config);
            return config;
        });
        
        services.AddScoped<ScriptEngineHost>();
    }

    public static void AddScriptingWithPythonNet(this IServiceCollection services)
    {
        services.AddScoped<ScriptEngineConfiguration>(_ => new ScriptEngineConfiguration());
        services.AddScoped<ScriptEngineHost>();
    }

    /// <summary>
    /// Add scripting with Python.NET using configuration from appsettings.json
    /// </summary>
    public static void AddScriptingWithConfiguredPythonNet(this IServiceCollection services)
    {
        services.AddScoped<ScriptEngineConfiguration>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var config = new ScriptEngineConfiguration();
            configuration?.GetSection("Scripting").Bind(config);
            return config;
        });
        
        services.AddScoped<ScriptEngineHost>();
    }

    /// <summary>
    /// Add scripting for runners with dynamic settings loading
    /// This allows runners to load Python paths from database configuration
    /// Requires RunnerSettingsService and ScriptEngineSettingsInitializer to be registered
    /// 
    /// Configuration Priority (later overrides earlier):
    /// 1. appsettings.json "Scripting" section (optional base configuration)
    /// 2. Database runner-specific settings (user-configured overrides)
    /// 
    /// Settings are loaded at application startup by ScriptEngineSettingsInitializer (IHostedService).
    /// This avoids async/sync deadlocks during service registration while ensuring settings
    /// are available before any script execution occurs.
    /// 
    /// Example appsettings.json:
    /// {
    ///   "Scripting": {
    ///     "AutoDetectPythonPaths": true,
    ///     "DefaultTimeoutSeconds": 120
    ///   }
    /// }
    /// </summary>
    public static void AddScriptingForRunner(this IServiceCollection services)
    {
        // Register configuration as Singleton so settings loaded at startup persist
        services.AddSingleton<ScriptEngineConfiguration>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var config = new ScriptEngineConfiguration();
            
            // Bind base configuration from appsettings.json (optional)
            // This allows environment-specific defaults (Dev/Production) without database changes
            configuration?.GetSection("Scripting").Bind(config);
            
            // Note: Runner-specific settings from database are loaded at startup
            // by ScriptEngineSettingsInitializer (IHostedService) after DI container is built.
            // Settings are loaded into this singleton instance and persist for app lifetime.
            
            return config;
        });
        
        services.AddScoped<ScriptEngineHost>();
    }

    /// <summary>
    /// Add scripting for desktop with dynamic settings loading
    /// This allows desktop to load Python paths from database configuration
    /// Requires DesktopSettingsService and DesktopScriptEngineSettingsInitializer to be registered
    /// 
    /// Configuration Priority (later overrides earlier):
    /// 1. appsettings.json "Scripting" section (optional base configuration)
    /// 2. Database desktop-specific settings (user-configured overrides)
    /// 
    /// Settings are loaded at application startup by DesktopScriptEngineSettingsInitializer (IHostedService).
    /// This avoids async/sync deadlocks during service registration while ensuring settings
    /// are available before any script execution occurs.
    /// 
    /// Example appsettings.json:
    /// {
    ///   "Scripting": {
    ///     "AutoDetectPythonPaths": true
    ///   }
    /// }
    /// </summary>
    public static void AddScriptingForDesktop(this IServiceCollection services)
    {
        // Register configuration as Singleton so settings loaded at startup persist
        services.AddSingleton<ScriptEngineConfiguration>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var config = new ScriptEngineConfiguration();
            
            // Bind base configuration from appsettings.json (optional)
            // This allows environment-specific defaults without database changes
            configuration?.GetSection("Scripting").Bind(config);
            
            // Note: Desktop-specific settings from database are loaded at startup
            // by DesktopScriptEngineSettingsInitializer (IHostedService) after DI container is built.
            // Settings are loaded into this singleton instance and persist for app lifetime.
            
            return config;
        });
        services.AddSingleton<ScriptEngineHost>();
    }
}
