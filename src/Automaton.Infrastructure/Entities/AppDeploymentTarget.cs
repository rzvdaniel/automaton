using Automaton.Api.Enums;

namespace Automaton.Api.Entities;

public sealed class AppDeploymentTarget
{
    public Guid Id { get; set; }

    public Guid WebAppId { get; set; }
    public WebApp WebApp { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;

    public Guid? ExecutionRunnerId { get; set; }
    public Runner? ExecutionRunner { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>
    /// Nullable marker used by the database unique constraint. MySQL permits
    /// multiple NULL values, so non-default targets use NULL and the default
    /// target uses 1.
    /// </summary>
    public byte? DefaultConstraintKey { get; private set; }

    /// <summary>
    /// Indicates that the deferred legacy controller runtime completed for
    /// this target. This marker is intentionally separate from
    /// <see cref="GenericRuntimeDeployed"/> and cannot make a generic route
    /// eligible.
    /// </summary>
    public bool LegacyControllerRuntimeDeployed { get; set; }

    /// <summary>
    /// Indicates that the canonical generic runtime contract was promoted by
    /// the generic runtime deployment path. Legacy controller deployment must
    /// not make a target routable through the generic runtime gateway.
    /// </summary>
    public bool GenericRuntimeDeployed { get; set; }

    /// <summary>
    /// Identifies the server-owned package deployment currently mutating this
    /// target, so an older operation cannot complete after a newer one.
    /// </summary>
    public Guid? ActiveDeploymentOperationId { get; set; }

    /// <summary>
    /// Identifies the execution attempt currently allowed to complete the
    /// active package operation. A retry may reuse the operation ID with a
    /// new attempt after the previous lease expires.
    /// </summary>
    public Guid? ActiveDeploymentExecutionAttemptId { get; set; }

    public DateTimeOffset? LastDeploymentAt { get; set; }
    public DateTimeOffset? LastSuccessfulDeploymentAt { get; set; }

    public DeploymentTargetStatus Status { get; set; } = DeploymentTargetStatus.NotDeployed;
    public bool CleanupPending { get; set; }
    public string? LastCleanupError { get; set; }
    public DateTimeOffset? LastCleanupAttemptAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void SynchronizeDefaultConstraint()
    {
        DefaultConstraintKey = IsDefault ? (byte)1 : null;
    }

    /// <summary>
    /// Creates the single primary target for a new app. Internally, <see cref="IsDefault"/>
    /// remains the persisted primary-target marker for backwards compatibility.
    /// </summary>
    public static AppDeploymentTarget CreateLive(Guid webAppId, Guid? runnerId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = webAppId,
            Name = "Live",
            Slug = "live",
            ExecutionRunnerId = runnerId,
            IsDefault = true,
            Status = runnerId.HasValue
                ? DeploymentTargetStatus.NotDeployed
                : DeploymentTargetStatus.Unassigned,
            CreatedAt = now,
            UpdatedAt = now
        };
        target.SynchronizeDefaultConstraint();
        return target;
    }

    // Kept for existing callers and historical test fixtures. New apps must use CreateLive.
    public static AppDeploymentTarget CreateDefault(Guid webAppId, Guid? runnerId = null) =>
        CreateLive(webAppId, runnerId);
}
