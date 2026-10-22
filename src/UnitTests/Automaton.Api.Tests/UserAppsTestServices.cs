using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace Automaton.Api.Tests;

internal static class UserAppsTestServices
{
    public static AppsService CreateAppsService(ApplicationDbContext dbContext, HttpContext httpContext)
    {
        var userContextService = new UserContextService(new HttpContextAccessor
        {
            HttpContext = httpContext
        });
        var authorizationService = new ResourceAuthorizationService(dbContext, userContextService);
        var runnerAuthorizationPolicy = new AppRunnerAuthorizationPolicy(dbContext, authorizationService);
        var runnerAssignmentPolicy = new AppRunnerAssignmentPolicy(
            dbContext,
            runnerAuthorizationPolicy,
            new AppDeploymentTargetResolver(dbContext, authorizationService, runnerAuthorizationPolicy));
        var auditEventService = new NoOpAuditEventService();
        var applicationEventService = new NoOpApplicationEventService();
        var environment = new StubWebHostEnvironment();

        return new AppsService(
            dbContext,
            userContextService,
            authorizationService,
            null!,
            new AppMetadataService(dbContext, userContextService, null!, auditEventService),
            new AppMutationService(
                dbContext,
                userContextService,
                authorizationService,
                null!,
                environment,
                new AppMutationAuditService(userContextService, null!, auditEventService),
                null!,
                runnerAssignmentPolicy,
                new AppDeletionService(dbContext, null!)),
            CreatePublicationService(
                dbContext, userContextService, authorizationService,
                runnerAssignmentPolicy, auditEventService, applicationEventService),
            new AppForkingService(
                dbContext,
                userContextService,
                null!,
                runnerAssignmentPolicy,
                null!,
                auditEventService),
            new AppQueryService(dbContext, userContextService, authorizationService, null!));
    }

    private static AppPublicationService CreatePublicationService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        ResourceAuthorizationService authorizationService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        IAuditEventService auditEventService,
        IApplicationEventService applicationEventService)
        => new(dbContext, userContextService, authorizationService, runnerAssignmentPolicy,
            auditEventService, applicationEventService, null!, null!);

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpApplicationEventService : IApplicationEventService
    {
        public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(
            Guid appId,
            ApplicationEventQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<ApplicationEventDto>
            {
                Items = [],
                TotalCount = 0,
                Page = query.Page,
                PageSize = query.PageSize
            });
    }
}
