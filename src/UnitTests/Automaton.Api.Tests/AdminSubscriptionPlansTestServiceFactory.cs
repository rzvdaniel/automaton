using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Messaging;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Api.Tests;

internal static class AdminSubscriptionPlansTestServiceFactory
{
    public static AdminSubscriptionPlansController CreateController(
        ApplicationDbContext dbContext,
        Guid actorUserId,
        ITransactionalEventExecutor? transactionalEventExecutor = null,
        IAuditEventService? auditEventService = null)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId);
        var userContextService = new UserContextService(httpContextAccessor);
        auditEventService ??= new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var planOperations = new AdminSubscriptionPlanOperationsService(
            dbContext,
            auditEventService,
            transactionalEventExecutor ?? new AdminPlanInlineTransactionalEventExecutor());
        return new AdminSubscriptionPlansController(dbContext, new AdminPlanTestUserManager(), planOperations);
    }

    public static IAuditEventService CreateAuditEventService(ApplicationDbContext dbContext, Guid actorUserId)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId);
        return new AuditEventService(dbContext, new UserContextService(httpContextAccessor), httpContextAccessor);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "admin@automaton.test")
            ], "Test"))
        };
        return new HttpContextAccessor { HttpContext = httpContext };
    }
}

internal sealed class AdminPlanInlineTransactionalEventExecutor(Action? onExecute = null) : ITransactionalEventExecutor
{
    public CancellationToken LastToken { get; private set; }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        LastToken = cancellationToken;
        onExecute?.Invoke();
        return operation(cancellationToken);
    }
}
