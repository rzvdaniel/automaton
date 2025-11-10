using Automaton.App.Account.Config;
using Automaton.App.Authentication.Config;
using Automaton.Client.Auth.Extensions;
using Automaton.Client.Auth.Interfaces;
using Automaton.Client.Auth.Services;
using Automaton.Core.Interfaces;
using Automaton.Core.Logs;
using Automaton.Core.Scripting;
using Automaton.Studio.Domain;
using Automaton.Studio.Factories;
using Automaton.Studio.Pages.AiApiKeys;
using Automaton.Studio.Pages.ApiKeys;
using Automaton.Studio.Pages.StepDesigner;
using Automaton.Studio.Pages.Flows;
using Automaton.Studio.Pages.Runners;
using Automaton.Studio.Services;
using Automaton.Studio.Shared;
using Automaton.Studio.Steps.EmitLog;
using Automaton.Studio.Steps.ExecuteFlow;
using Automaton.Studio.Steps.ExecutePython;
using Automaton.Studio.Steps.If;
using Automaton.Studio.Steps.Sequence;
using Automaton.Studio.Steps.While;
using Blazored.LocalStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using Automaton.Mcp.Client.Extensions;
using Automaton.Mcp.Client.Services;
using Automaton.Studio.Services.Mcp;
using Automaton.Studio.Config;
using Automaton.Studio.Pages.Flows.FlowDesigner;
using Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;
using Automaton.Studio.Pages.Flows.Designer;

namespace Automaton.Studio.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddStudio(this IServiceCollection services, IConfiguration configuration)
    {
        var configService = new Services.ConfigurationService(configuration);

        services.AddInstallApp(configuration);
        services.AddAccountApp(configuration);
        services.AddAuthenticationApp(configuration);

        services.AddAutomatonCore();
        services.AddAutomatonSteps();
        services.AddScoped<StepTypeDescriptor>();
        services.AddScoped<StepFactory>();

        // Scripting - Desktop settings are loaded by CustomStepExecuteService at execution time
        // This avoids async/sync deadlocks during service registration
        services.AddScriptingForDesktop();

        // Authentication & Authorization
        services.AddBlazoredLocalStorage();
        services.AddAuthorizationCore();
        services.AddStudioAuthentication(configuration);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));
        services.AddCourier(Assembly.GetExecutingAssembly());
        services.AddScoped<IAuthenticationStorage, WebAuthenticationStorage>();

        // Register MCP services
        services.AddAutomatonMcpClient();
        services.AddScoped<IMcpToolProvider, StudioMcpToolProvider>();
        services.AddScoped<ICustomStepExecuteService, CustomStepExecuteService>();
        services.AddScoped<ICurrentStepContextService, CurrentStepContextService>();

        // Services
        services.AddScoped<Services.ConfigurationService>();
        services.AddScoped<DesktopSettingsService>();
        
        // Register initializer as singleton and as hosted service
        services.AddSingleton<DesktopScriptEngineSettingsInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<DesktopScriptEngineSettingsInitializer>());
        
        services.AddScoped<OnboardingService>();
        services.AddScoped<UserAccountService>();
        services.AddScoped<IApiKeysService, ApiKeysService>();
        services.AddScoped<AiApiKeyService>();
        services.AddScoped<AiStepGenerationService>();
        services.AddScoped<FlowService>();
        services.AddScoped<FlowsService>();
        services.AddScoped<RunnerService>();
        services.AddScoped<ErrorService>();
        services.AddScoped<FlowExecutionsService>();
        services.AddScoped<CustomStepsService>();
        services.AddScoped<Services.AppsService>();
        services.AddScoped<IframeProxyService>();
        services.AddScoped<IframeMessageHandler>();
        services.AddScoped<StudioFlowExecuteService>();
        services.AddScoped<StudioFlowConvertService>(); // Keep concrete for backwards compatibility
        services.AddScoped<IFlowConvertService<StudioFlow>, StudioFlowConvertService>(); // Add interface registration
        services.AddScoped<FlowLogsService>();
        services.AddScoped<FlowScheduleService>();
        services.AddScoped<CustomStepExecuteService>();
        services.AddScoped<FlowSettingsService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<RecentActivityService>();
        services.AddScoped<AiCodeCleanupService>();
        services.AddScoped<CustomStepVersionService>();
        services.AddScoped<ICodeExtractionService, CodeExtractionService>();
        services.AddScoped<ITextSimilarityService, TextSimilarityService>();
        services.AddScoped<IAiMessageFormattingService, AiMessageFormattingService>();

        // Add Studio-specific persistence service
        services.AddScoped<StudioWorkflowExecutionPersistenceService>();
        services.AddScoped<IWorkflowExecutionPersistenceService, StudioWorkflowExecutionPersistenceService>();

        // ViewModels
        services.AddScoped<FlowsViewModel>();
        services.AddScoped<RunnersViewModel>();
        services.AddScoped<ApiKeysViewModel>();
        services.AddScoped<AiApiKeysViewModel>();
        services.AddScoped<Pages.CustomSteps.CustomStepsViewModel>();
        services.AddScoped<Pages.Apps.AppsViewModel>();
        services.AddScoped<DesignerViewModel>();
        services.AddScoped<StepViewModel>();
        services.AddScoped<StepsViewModel>();
        services.AddScoped<CoreStepsViewModel>();
        services.AddScoped<CustomStepsViewModel>();
        services.AddScoped<MainLayoutViewModel>();
        services.AddScoped<FlowActivityViewModel>();
        services.AddScoped<FlowLogsViewModel>();
        services.AddScoped<FlowScheduleViewModel>();
        services.AddScoped<FlowSettingsViewModel>();

        // Studio steps
        services.AddTransient<EmitLogStep>();
        services.AddTransient<ExecutePythonStep>();
        services.AddTransient<ExecuteFlowStep>();
        services.AddTransient<SequenceStep>();
        services.AddTransient<SequenceEndStep>();
        services.AddTransient<IfStep>();
        services.AddTransient<WhileStep>();
        services.AddTransient<Steps.Custom.CustomStep>();

        // Models
        services.AddScoped<AppConfig>();
        services.AddScoped<ApiConfig>();

        // Javascript
        services.AddScoped<JsInterop>();

        services.AddSingleton<WorkflowLogsManager>();
        services.AddTransient<Handlers.LogUpdateNotificationHandler>();

        // Other
        services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(configService.BaseUrl) });
        services.AddSingleton(sp => new SerilogHttpClient(new HttpClient { BaseAddress = new Uri(configService.BaseUrl) }));
        services.AddSingleton<KeyboardService>();

        services.AddScoped(typeof(DragDropService));

        services.AddLogging(x =>
        {
            x.ClearProviders();
            x.AddSerilog(dispose: true);
        });

#if DEBUG
        Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine(msg));
        Serilog.Debugging.SelfLog.Enable(Console.Error);
#endif
    }
}
