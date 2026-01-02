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
using Automaton.Mcp.Services;
using Automaton.Studio.Config;
using Automaton.Studio.Pages.Flows.FlowDesigner;
using Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;
using Automaton.Studio.Pages.Flows.Designer;
using Automaton.Studio.Pages.Steps.Designer;
using Automaton.Studio.Pages.Account.Models;
using Automaton.Studio.Pages.Authentication.Config;
using Automaton.Studio.Pages.Install.Config;
using Automaton.Studio.Pages.Dashboard;
using Automaton.Studio.Pages.Flows.Components;
using Automaton.Studio.Pages.Flows.Designer.Components.FlowSchedule;
using Automaton.Studio.Pages.Flows.Designer.Components.FlowSettings;
using Automaton.Studio.Pages.Flows.Designer.Components.FlowActivity;
using Automaton.Studio.Services.Mcp;
using Automaton.Studio.Services.Mcp.Clients;
using Automaton.Studio.Services.Mcp.Clients.Providers;
using Automaton.Studio.Services.Mcp.ToolProviders;
using Automaton.Studio.Services.Mcp.EditorBridges;
using Automaton.Studio.Services.Mcp.SystemPrompts;
using Automaton.Studio.Services.Admin;
using Automaton.Studio.Pages.Admin.AiProviders;
using Automaton.Studio.Pages.Admin.AiModels;

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

        // Register AI Provider Registry as singleton - holds all registered provider implementations
        services.AddSingleton<IAiProviderRegistry>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var registry = new AiProviderRegistry();

            // Explicit registration of AI providers
            var openAiProvider = new OpenAiProvider(loggerFactory.CreateLogger<OpenAiProvider>());
            registry.Register(openAiProvider);

            var anthropicProvider = new AnthropicProvider(loggerFactory.CreateLogger<AnthropicProvider>());
            registry.Register(anthropicProvider);

            // Future providers can be added here:
            // registry.Register(new GoogleProvider(loggerFactory.CreateLogger<GoogleProvider>()));

            return registry;
        });

        // Register MCP services - Tool providers and explicit context clients
        services.AddScoped<StepMcpToolProvider>();
        services.AddScoped<AppMcpToolProvider>();
        services.AddScoped<UnifiedAppMcpToolProvider>();

        // Register context-specific MCP clients
        services.AddScoped<IStepMcpClient, StepMcpClient>();
        services.AddScoped<IAppMcpClient, AppMcpClient>();
        services.AddScoped<IUnifiedAppMcpClient, UnifiedAppMcpClient>();
        
        services.AddScoped<IStepSystemPromptBuilder, StepSystemPromptBuilder>();
        services.AddScoped<IAppSystemPromptBuilder, AppSystemPromptBuilder>();
        services.AddScoped<IUnifiedAppSystemPromptBuilder, UnifiedAppSystemPromptBuilder>();
        services.AddScoped<ICustomStepExecuteService, CustomStepExecuteService>();
        services.AddScoped<IStepEditorBridge, StepEditorBridge>();
        services.AddScoped<IAppEditorBridge, AppEditorBridge>();

        // Services
        services.AddScoped<Services.ConfigurationService>();
        services.AddScoped<DesktopSettingsService>();
        services.AddScoped<IClaimUtilityService, ClaimUtilityService>();

        // Register initializer as singleton and as hosted service
        services.AddSingleton<DesktopScriptEngineSettingsInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<DesktopScriptEngineSettingsInitializer>());
        
        services.AddScoped<OnboardingService>();
        services.AddScoped<UserAccountService>();
        services.AddScoped<IApiKeysService, ApiKeysService>();
        services.AddScoped<AiApiKeyService>();
        services.AddScoped<FlowService>();
        services.AddScoped<FlowsService>();
        services.AddScoped<RunnerService>();
        services.AddScoped<ErrorService>();
        services.AddScoped<FlowExecutionsService>();
        services.AddScoped<CustomStepsService>();
        services.AddScoped<AppsService>();
        services.AddScoped<AppStepsService>();
        services.AddScoped<IframeProxyService>();
        services.AddScoped<IframeMessageHandler>();
        services.AddScoped<StudioFlowExecuteService>();
        services.AddScoped<StudioFlowConvertService>();
        services.AddScoped<IFlowConvertService<StudioFlow>, StudioFlowConvertService>();
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
        services.AddScoped<ApiKeysService>();
        services.AddScoped<AiApiKeyService>();
        services.AddScoped<AiChatHistoryService>();

        // Project services
        services.AddScoped<ProjectsService>();
        services.AddScoped<ProjectMembersService>();
        services.AddScoped<ProjectResourcesService>();
        services.AddScoped<ProjectContextService>();
        services.AddScoped<DashboardContextService>();
        services.AddScoped<ResourceAuthorizationService>();

        // Admin services
        services.AddScoped<AdminAiProviderService>();

        // Add Studio-specific persistence service
        services.AddScoped<StudioWorkflowExecutionPersistenceService>();
        services.AddScoped<IWorkflowExecutionPersistenceService, StudioWorkflowExecutionPersistenceService>();

        // ViewModels
        services.AddScoped<FlowsViewModel>();
        services.AddScoped<RunnersViewModel>();
        services.AddScoped<ApiKeysViewModel>();
        services.AddScoped<AiApiKeysViewModel>();
        services.AddScoped<Pages.Steps.CustomStepsViewModel>();
        services.AddScoped<Pages.Apps.AppsViewModel>();
        services.AddScoped<Pages.Projects.ProjectsViewModel>();
        services.AddScoped<Pages.Projects.ProjectViewModel>();
        services.AddScoped<DesignerViewModel>();
        services.AddScoped<StepViewModel>();
        services.AddScoped<StepsViewModel>();
        services.AddScoped<CoreStepsViewModel>();
        services.AddScoped<CustomStepsViewModel>();
        services.AddScoped<MainLayoutViewModel>();
        services.AddScoped<FlowActivityViewModel>();
        services.AddScoped<FlowScheduleViewModel>();
        services.AddScoped<FlowSettingsViewModel>();
        services.AddScoped<DashboardViewModel>();

        // Admin ViewModels
        services.AddScoped<AdminAiProvidersViewModel>();
        services.AddScoped<AdminAiModelsViewModel>();

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
        // Register a scoped HttpClient for all services
        // All services (FlowSettingsService, ProjectsClientService, etc.) that accept
        // HttpClient in their constructor will receive this instance with BaseAddress set
        services.AddScoped(sp =>
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(configService.BaseUrl)
            };
            return client;
        });

        // Special registration for AuthenticationService
        services.AddHttpClient<Pages.Authentication.Services.AuthenticationService>(client =>
        {
            client.BaseAddress = new Uri(configService.BaseUrl);
        });

        // Scoped HttpClient for logging (SerilogHttpClient)
        services.AddHttpClient<SerilogHttpClient>(client =>
        {
            client.BaseAddress = new Uri(configService.BaseUrl);
        });

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
