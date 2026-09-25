using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Automaton.Api.Data
{
    public class ApplicationDbContext : ApplicationDbContextEntitySetBase2, IDataContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        private IDbContextTransaction _transaction;


        // GitHub integration

        // Project/Organization entities

        // Organization entities

        // AI Chat History entities

        // User Settings

        // Subscription plans

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // No-op, configuration is handled by DI
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            CoreModelConfiguration.Apply(modelBuilder);
            RunnerModelConfiguration.Apply(modelBuilder);
            RunnerStorageModelConfiguration.Apply(modelBuilder);
            SchedulingModelConfiguration.Apply(modelBuilder);
            ApplicationModelConfiguration.Apply(modelBuilder);
            IdentityAiModelConfiguration.Apply(modelBuilder);
            ProjectModelConfiguration.Apply(modelBuilder);
            ChatOrganizationModelConfiguration.Apply(modelBuilder);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepareDeploymentTargets();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            await PrepareDeploymentTargetsAsync(cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void PrepareDeploymentTargets()
        {
            foreach (var appEntry in ChangeTracker.Entries<WebApp>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                var app = appEntry.Entity;
                if (!app.DeploymentTargets.Any(target => target.IsDefault))
                {
                    app.DeploymentTargets.Add(AppDeploymentTarget.CreateLive(app.Id));
                }
            }

            foreach (var targetEntry in ChangeTracker.Entries<AppDeploymentTarget>()
                         .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                targetEntry.Entity.SynchronizeDefaultConstraint();
            }

            ValidateDefaultTargetCounts();
        }

        private async Task PrepareDeploymentTargetsAsync(CancellationToken cancellationToken)
        {
            foreach (var appEntry in ChangeTracker.Entries<WebApp>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                var app = appEntry.Entity;
                if (!app.DeploymentTargets.Any(target => target.IsDefault))
                {
                    app.DeploymentTargets.Add(AppDeploymentTarget.CreateLive(app.Id));
                }
            }

            foreach (var targetEntry in ChangeTracker.Entries<AppDeploymentTarget>()
                         .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                targetEntry.Entity.SynchronizeDefaultConstraint();
            }

            await ValidateDefaultTargetCountsAsync(cancellationToken);
        }

        private void ValidateDefaultTargetCounts()
        {
            var targetEntries = ChangeTracker.Entries<AppDeploymentTarget>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray();

            foreach (var appId in targetEntries.Select(entry => entry.Entity.WebAppId).Distinct())
            {
                ValidateAppDefaultTargetCount(appId, targetEntries);
            }
        }

        private void ValidateAppDefaultTargetCount(
            Guid appId,
            IReadOnlyCollection<EntityEntry<AppDeploymentTarget>> targetEntries)
        {
            if (IsAppBeingDeleted(appId))
                return;

            var defaultCount = AppDeploymentTargets
                .AsNoTracking()
                .Count(target => target.WebAppId == appId && target.IsDefault);
            foreach (var entry in targetEntries.Where(entry => entry.Entity.WebAppId == appId))
                defaultCount += GetDefaultTargetChange(entry);

            EnsureExactlyOneDefaultTarget(defaultCount);
        }

        private async Task ValidateDefaultTargetCountsAsync(CancellationToken cancellationToken)
        {
            var targetEntries = ChangeTracker.Entries<AppDeploymentTarget>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray();

            foreach (var appId in targetEntries.Select(entry => entry.Entity.WebAppId).Distinct())
            {
                await ValidateAppDefaultTargetCountAsync(appId, targetEntries, cancellationToken);
            }
        }

        private async Task ValidateAppDefaultTargetCountAsync(
            Guid appId,
            IReadOnlyCollection<EntityEntry<AppDeploymentTarget>> targetEntries,
            CancellationToken cancellationToken)
        {
            if (IsAppBeingDeleted(appId))
                return;

            var defaultCount = await AppDeploymentTargets
                .AsNoTracking()
                .CountAsync(
                    target => target.WebAppId == appId && target.IsDefault,
                    cancellationToken);
            foreach (var entry in targetEntries.Where(entry => entry.Entity.WebAppId == appId))
                defaultCount += GetDefaultTargetChange(entry);

            EnsureExactlyOneDefaultTarget(defaultCount);
        }

        private bool IsAppBeingDeleted(Guid appId) => ChangeTracker.Entries<WebApp>().Any(entry =>
            entry.Entity.Id == appId && entry.State == EntityState.Deleted);

        private static int GetDefaultTargetChange(EntityEntry<AppDeploymentTarget> entry)
        {
            var wasDefault = entry.State != EntityState.Added &&
                             entry.OriginalValues.GetValue<bool>(nameof(AppDeploymentTarget.IsDefault));
            var isDefault = entry.State != EntityState.Deleted && entry.Entity.IsDefault;
            return (isDefault ? 1 : 0) - (wasDefault ? 1 : 0);
        }

        private static void EnsureExactlyOneDefaultTarget(int defaultCount)
        {
            if (defaultCount != 1)
                throw new InvalidOperationException(
                    "Every App must have exactly one primary deployment target.");
        }

        public void BeginTransaction()
        {
            _transaction = Database.BeginTransaction();
        }

        public int Commit()
        {
            try
            {
                var saveChanges = SaveChanges();
                _transaction.Commit();
                return saveChanges;
            }
            finally
            {
                _transaction.Dispose();
            }
        }

        public void Rollback()
        {
            _transaction.Rollback();
            _transaction.Dispose();
        }

        public Task<int> CommitAsync()
        {
            try
            {
                var saveChangesAsync = SaveChangesAsync();
                _transaction.Commit();
                return saveChangesAsync;
            }
            finally
            {
                _transaction.Dispose();
            }
        }
    }
}
