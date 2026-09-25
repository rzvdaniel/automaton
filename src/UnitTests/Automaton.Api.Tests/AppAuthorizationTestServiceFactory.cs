using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class AppAuthorizationTestServiceFactory
{
    public static AppsService CreateAppsService(
        ApplicationDbContext db,
        Guid userId,
        string? contentRootPath,
        out AppFileStorageService storage)
    {
        var dependencies = CreateDependencies(db, userId, contentRootPath);
        storage = dependencies.Storage;
        return ComposeAppsService(dependencies);
    }

    public static AppsService CreateAppsService(ApplicationDbContext db, Guid userId, string? contentRootPath = null)
    {
        var dependencies = CreateDependencies(db, userId, contentRootPath);
        return ComposeAppsService(dependencies);
    }

    public static AppsStepExecutionService CreateAppsStepExecutionService(ApplicationDbContext db) =>
        new(db, null!, null!);

    private static AppsServiceDependencies CreateDependencies(
        ApplicationDbContext db,
        Guid userId,
        string? contentRootPath)
    {
        var userContext = CreateUserContextService(userId);
        var authorization = new ResourceAuthorizationService(db, userContext);
        var environment = new AppAuthorizationTestWebHostEnvironment(contentRootPath ?? Path.GetTempPath());
        var settings = new SettingsService(
            db,
            userContext,
            Options.Create(new AppFileRuleBaselineOptions
            {
                RemixSourceExclude = [".env", ".env.*", "secrets.json", "secrets", "secrets/**"],
                RemixFrontendExclude = []
            }));
        var storage = new AppFileStorageService(environment, settings);
        var assignment = CreateRunnerAssignmentPolicy(db, authorization);
        var routes = new BackendRouteConfigService(
            db, Options.Create(new PublishedAppsConfig()), environment,
            NullLogger<BackendRouteConfigService>.Instance);

        return new AppsServiceDependencies(
            db, userContext, authorization, environment, storage, assignment, routes,
            new RecentActivityService(db, userContext),
            new AppAuthorizationNoOpAuditEventService(),
            new AppAuthorizationNoOpApplicationEventService());
    }

    private static UserContextService CreateUserContextService(Guid userId) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
        }
    });

    private static AppRunnerAssignmentPolicy CreateRunnerAssignmentPolicy(
        ApplicationDbContext db,
        ResourceAuthorizationService authorization)
    {
        var runnerAuthorization = new AppRunnerAuthorizationPolicy(db, authorization);
        return new AppRunnerAssignmentPolicy(
            db,
            new HostedRunnerAppCapacityService(db),
            runnerAuthorization,
            new AppDeploymentTargetResolver(db, authorization, runnerAuthorization));
    }

    private static AppsService ComposeAppsService(AppsServiceDependencies dependencies) =>
        new(
            dependencies.Db,
            dependencies.UserContext,
            dependencies.Authorization,
            dependencies.Storage,
            new AppMetadataService(dependencies.Db, dependencies.UserContext, dependencies.Storage, dependencies.Audit),
            CreateMutationService(dependencies),
            CreatePublicationService(dependencies),
            CreateForkingService(dependencies),
            new AppQueryService(dependencies.Db, dependencies.UserContext, dependencies.Authorization, dependencies.Storage));

    private static AppMutationService CreateMutationService(AppsServiceDependencies dependencies) =>
        new(
            dependencies.Db,
            dependencies.UserContext,
            dependencies.Authorization,
            dependencies.Storage,
            dependencies.Environment,
            new AppMutationAuditService(dependencies.UserContext, dependencies.RecentActivity, dependencies.Audit),
            dependencies.Routes,
            dependencies.Assignment,
            new AppDeletionService(dependencies.Db, dependencies.Storage));

    private static AppPublicationService CreatePublicationService(AppsServiceDependencies dependencies) =>
        new(
            dependencies.Db,
            dependencies.UserContext,
            dependencies.Authorization,
            dependencies.Assignment,
            dependencies.Audit,
            dependencies.ApplicationEvents,
            dependencies.RecentActivity,
            dependencies.Routes);

    private static AppForkingService CreateForkingService(AppsServiceDependencies dependencies) =>
        new(
            dependencies.Db,
            dependencies.UserContext,
            dependencies.Storage,
            dependencies.Assignment,
            dependencies.RecentActivity,
            dependencies.Audit);

    private sealed record AppsServiceDependencies(
        ApplicationDbContext Db,
        UserContextService UserContext,
        ResourceAuthorizationService Authorization,
        IWebHostEnvironment Environment,
        AppFileStorageService Storage,
        AppRunnerAssignmentPolicy Assignment,
        BackendRouteConfigService Routes,
        RecentActivityService RecentActivity,
        IAuditEventService Audit,
        IApplicationEventService ApplicationEvents);
}

internal sealed class AppAuthorizationTestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Automaton.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; } = contentRootPath;
    public string EnvironmentName { get; set; } = "Production";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

internal sealed class AppAuthorizationNoOpAuditEventService : IAuditEventService
{
    public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        string message,
        object? metadata = null,
        string outcome = "Success",
        bool failOnError = false,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class AppAuthorizationNoOpApplicationEventService : IApplicationEventService
{
    public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(
        Guid appId,
        ApplicationEventQuery query,
        CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<ApplicationEventDto>
        {
            Items = [],
            TotalCount = 0,
            Page = query.Page,
            PageSize = query.PageSize
        });
}
