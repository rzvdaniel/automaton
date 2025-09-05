using AutoMapper;
using Automaton.App.Account.Config;
using Automaton.App.Authentication.Config;
using Automaton.Client.Auth.Extensions;
using Automaton.Client.Auth.Interfaces;
using Automaton.Client.Auth.Services;
using Automaton.Core.Logs;
using Automaton.Core.Scripting;
using Automaton.Studio.Domain;
using Automaton.Studio.Factories;
using Automaton.Studio.Mapper;
using Automaton.Studio.Pages.CustomStepDesigner;
using Automaton.Studio.Pages.CustomSteps;
using Automaton.Studio.Pages.FlowDesigner;
using Automaton.Studio.Pages.FlowDesigner.Components.StepExplorer;
using Automaton.Studio.Pages.Flows;
using Automaton.Studio.Pages.Runners;
using Automaton.Studio.Pages.ApiKeys;
using Automaton.Studio.Services;
using Automaton.Studio.Shared;
using Automaton.Studio.Steps.AddVariable;
using Automaton.Studio.Steps.EmitLog;
using Automaton.Studio.Steps.ExecuteFlow;
using Automaton.Studio.Steps.ExecutePython;
using Automaton.Studio.Steps.If;
using Automaton.Studio.Steps.Sequence;
using Automaton.Studio.Steps.Test;
using Automaton.Studio.Steps.TestAssert;
using Automaton.Studio.Steps.TestReport;
using Automaton.Studio.Steps.While;
using Blazored.LocalStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace Automaton.Studio.Config;

public static class SeviceCollection
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

        // Scripting
        services.AddScripting();

        // Authentication & Authorization
        services.AddBlazoredLocalStorage();
        services.AddAuthorizationCore();
        services.AddStudioAuthentication(configuration);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));
        services.AddCourier(Assembly.GetExecutingAssembly());
        services.AddScoped<IAuthenticationStorage, WebAuthenticationStorage>();

        // Services
        services.AddScoped<Services.ConfigurationService>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<UserAccountService>();
        services.AddScoped<IApiKeysService, ApiKeysService>();
        services.AddScoped<FlowService>();
        services.AddScoped<FlowsService>();
        services.AddScoped<RunnerService>();
        services.AddScoped<ErrorService>();
        services.AddScoped<FlowExecutionsService>();
        services.AddScoped<CustomStepsService>();
        services.AddScoped<StudioFlowExecuteService>();
        services.AddScoped<StudioFlowConvertService>();
        services.AddScoped<FlowLogsService>();
        services.AddScoped<FlowScheduleService>();
        services.AddScoped<CustomStepExecuteService>();
        services.AddScoped<FlowSettingsService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<RecentActivityService>();

        // ViewModels
        services.AddScoped<FlowsViewModel>();
        services.AddScoped<RunnersViewModel>();
        services.AddScoped<ApiKeysViewModel>();
        services.AddScoped<Pages.CustomSteps.CustomStepsViewModel>();
        services.AddScoped<DesignerViewModel>();
        services.AddScoped<CustomStepViewModel>();
        services.AddScoped<StepsViewModel>();
        services.AddScoped<CoreStepsViewModel>();
        services.AddScoped<Pages.FlowDesigner.Components.StepExplorer.CustomStepsViewModel>();
        services.AddScoped<MainLayoutViewModel>();
        services.AddScoped<FlowActivityViewModel>();
        services.AddScoped<FlowLogsViewModel>();
        services.AddScoped<FlowScheduleViewModel>();
        services.AddScoped<FlowSettingsViewModel>();

        // Studio steps
        // Note: Must be transient for some reason
        services.AddTransient<EmitLogStep>();
        services.AddTransient<AddVariableStep>();
        services.AddTransient<ExecutePythonStep>();
        services.AddTransient<ExecuteFlowStep>();
        services.AddTransient<TestStep>();
        services.AddTransient<TestAssertStep>();
        services.AddTransient<TestReportStep>();
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

        // Other
        services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(configService.BaseUrl) });
        services.AddSingleton(sp => new SerilogHttpClient(new HttpClient { BaseAddress = new Uri(configService.BaseUrl) }));
        services.AddSingleton(sp => new WorkflowLogsSink());
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

        services.AddScoped(provider => {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var configExpression = new MapperConfigurationExpression();
            configExpression.AddProfile(new AutoMapperProfile(provider));
            return new MapperConfiguration(configExpression, loggerFactory).CreateMapper();
        });
    }
}
