using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class DeploymentDispatchServices
{
    public static ApplicationDbContext CreateCallbackContext(ApplicationDbContext db)
        => new((DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>());

    public static DeploymentOperationService CreateOperations(ApplicationDeploymentServiceTests.DeploymentFixture fixture,
        IHostedRunnerDeploymentCommandDispatcher probe, ApplicationDbContext db, IServiceScopeFactory scopes) => new(
        db, null!, null!, null!,
        new DeploymentPackageStore(new TestWebHostEnvironment { ContentRootPath = fixture.RootPath },
            Options.Create(new DeploymentPackageStorageOptions())),
        fixture.Service, probe, fixture.ApiIdentity, scopes, NullLogger<DeploymentOperationService>.Instance);
}
