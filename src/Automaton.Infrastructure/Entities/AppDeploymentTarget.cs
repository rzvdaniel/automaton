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

    public bool FrontendDeployed { get; set; }
    public bool BackendDeployed { get; set; }

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

    public static AppDeploymentTarget CreateDefault(Guid webAppId, Guid? runnerId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = webAppId,
            Name = "Default",
            Slug = "default",
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
}
