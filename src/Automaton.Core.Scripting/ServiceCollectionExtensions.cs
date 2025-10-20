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
        
        services.AddScoped<ScriptEngineFactory>();
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
        
        services.AddScoped<ScriptEngineFactory>();
        services.AddScoped<ScriptEngineHost>();
    }

    public static void AddScriptingWithPythonNet(this IServiceCollection services, string? pythonDllPath = null)
    {
        services.AddScoped<ScriptEngineConfiguration>(_ => new ScriptEngineConfiguration 
        { 
            UsePythonNet = true,
            PythonDllPath = pythonDllPath
        });
        services.AddScoped<ScriptEngineFactory>();
        services.AddScoped<ScriptEngineHost>();
    }

    /// <summary>
    /// Add scripting with Python.NET using configuration from appsettings.json
    /// Example configuration:
    /// {
    ///   "Scripting": {
    ///     "UsePythonNet": true,
    ///     "PythonDllPath": "C:\\Python311\\python311.dll",
    ///     "EnableDebugMode": false
    ///   }
    /// }
    /// </summary>
    public static void AddScriptingWithConfiguredPythonNet(this IServiceCollection services)
    {
        services.AddScoped<ScriptEngineConfiguration>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var config = new ScriptEngineConfiguration
            {
                UsePythonNet = true // Force Python.NET usage
            };
            configuration?.GetSection("Scripting").Bind(config);
            return config;
        });
        
        services.AddScoped<ScriptEngineFactory>();
        services.AddScoped<ScriptEngineHost>();
    }

    /// <summary>
    /// Add scripting for runners with dynamic settings loading
    /// This allows runners to load Python paths from database configuration
    /// Requires RunnerSettingsService to be registered
    /// </summary>
    /// <param name="loadSettingsAsync">Function to load runner settings asynchronously</param>
    public static void AddScriptingForRunner(
        this IServiceCollection services, 
        Func<IServiceProvider, Task<RunnerScriptEngineSettings?>> loadSettingsAsync)
    {
        services.AddScoped<ScriptEngineConfiguration>(provider =>
        {
            var configuration = provider.GetService<IConfiguration>();
            var config = new ScriptEngineConfiguration
            {
                UsePythonNet = true, // Runners use Python.NET
                EnableDebugMode = false
            };
            
            // Bind from appsettings.json first
            configuration?.GetSection("Scripting").Bind(config);
            
            // Load runner-specific settings and apply them
            // This runs synchronously in the factory but uses async internally
            try
            {
                var settings = loadSettingsAsync(provider).GetAwaiter().GetResult();
                config.LoadFromRunnerSettings(settings);
            }
            catch (Exception ex)
            {
                // Log error but continue with default configuration
                Console.WriteLine($"Warning: Failed to load runner settings - {ex.Message}");
            }
            
            return config;
        });
        
        services.AddScoped<ScriptEngineFactory>();
        services.AddScoped<ScriptEngineHost>();
    }
}
