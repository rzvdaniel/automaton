using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Controllers.Admin;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AccountStorageProjectAllocationServiceTests
{
    [Test]
    public async Task GetOrCreateAsync_PersistsOnePositiveUniqueProjectPerAccount()
    {
        using var db = CreateDbContext();
        var first = CreateAccount();
        var second = CreateAccount();
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();

        var service = new AccountStorageProjectAllocationService(db);
        var firstProject = await service.GetOrCreateAsync(first.Id);
        var repeatedProject = await service.GetOrCreateAsync(first.Id);
        var secondProject = await service.GetOrCreateAsync(second.Id);

        Assert.Multiple(() =>
        {
            Assert.That(firstProject, Is.GreaterThan(0));
            Assert.That(repeatedProject, Is.EqualTo(firstProject));
            Assert.That(secondProject, Is.GreaterThan(0).And.Not.EqualTo(firstProject));
            Assert.That(db.AccountStorageAllocations.Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetOrCreateAsync_AdoptsOneExistingHostedRunnerProjectId()
    {
        using var db = CreateDbContext();
        var account = CreateAccount();
        var specification = CreateSpecification();
        db.Users.Add(account);
        db.HostedRunnerSpecifications.Add(specification);
        db.HostedRunners.Add(CreateHostedRunner(account.Id, specification.Id, 1_234_567));
        await db.SaveChangesAsync();

        var projectId = await new AccountStorageProjectAllocationService(db).GetOrCreateAsync(account.Id);

        Assert.That(projectId, Is.EqualTo(1_234_567));
        Assert.That(await db.AccountStorageAllocations
            .Where(x => x.AccountId == account.Id)
            .Select(x => x.StorageProjectId)
            .SingleAsync(), Is.EqualTo(projectId));
    }

    [Test]
    public async Task GetOrCreateAsync_FailsExplicitlyForConflictingHistoricalProjects()
    {
        using var db = CreateDbContext();
        var account = CreateAccount();
        var specification = CreateSpecification();
        db.Users.Add(account);
        db.HostedRunnerSpecifications.Add(specification);
        db.HostedRunners.AddRange(
            CreateHostedRunner(account.Id, specification.Id, 1_234_567),
            CreateHostedRunner(account.Id, specification.Id, 1_234_568));
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => new AccountStorageProjectAllocationService(db).GetOrCreateAsync(account.Id));

        Assert.That(exception!.Message, Does.Contain("conflicting historical"));
        Assert.That(await db.AccountStorageAllocations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task GetOrCreateAsync_AllowsMultipleHostedRunnersToShareTheAccountProject()
    {
        using var db = CreateDbContext();
        var account = CreateAccount();
        var specification = CreateSpecification();
        db.Users.Add(account);
        db.HostedRunnerSpecifications.Add(specification);
        await db.SaveChangesAsync();

        var service = new AccountStorageProjectAllocationService(db);
        var projectId = await service.GetOrCreateAsync(account.Id);
        db.HostedRunners.AddRange(
            CreateHostedRunner(account.Id, specification.Id, projectId),
            CreateHostedRunner(account.Id, specification.Id, projectId));
        await db.SaveChangesAsync();

        Assert.That(await service.GetOrCreateAsync(account.Id), Is.EqualTo(projectId));
        Assert.That(await db.HostedRunners.Select(x => x.StorageProjectId).Distinct().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task StorageProjectId_IsUniqueAcrossAccountAllocations()
    {
        using var db = CreateDbContext();
        var allocationType = db.Model.FindEntityType(typeof(AccountStorageAllocation))!;
        Assert.That(allocationType.GetIndexes().Single(x => x.Properties.Single().Name == nameof(AccountStorageAllocation.StorageProjectId)).IsUnique, Is.True);
    }

    [Test]
    public void HostedRunnerProvisioning_DoesNotAllocatePerRunnerOrAcceptClientStorageIdentity()
    {
        var parameterTypes = typeof(HostedRunnerProvisioningService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(parameterTypes, Does.Contain(typeof(IAccountStorageProjectAllocationService)));
            Assert.That(typeof(HostedRunnerProvisioningService).GetMethod(
                "AllocateStorageProjectIdAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), Is.Null);
            Assert.That(typeof(ProvisionHostedRunnerRequest).GetProperties()
                .Any(x => x.Name == nameof(HostedRunner.StorageProjectId)), Is.False);
        });
    }

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ApplicationUser CreateAccount()
    {
        var id = Guid.NewGuid();
        return new ApplicationUser
        {
            Id = id,
            Email = $"{id:N}@storage.test",
            UserName = id.ToString("N"),
            PublicUsername = $"user-{id.ToString("N")[..12]}",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }

    private static HostedRunnerSpecification CreateSpecification()
        => new()
        {
            Id = Guid.NewGuid(),
            Code = "storage-test",
            Name = "Storage test",
            Resources = new HostedRunnerResources()
        };

    private static HostedRunner CreateHostedRunner(Guid accountId, Guid specificationId, int projectId)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = accountId,
            Name = "Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            HostedRunnerSpecificationId = specificationId,
            ContainerName = $"automaton-{Guid.NewGuid():N}",
            StorageProjectId = projectId,
            CreatedAt = DateTime.UtcNow
        };
}
