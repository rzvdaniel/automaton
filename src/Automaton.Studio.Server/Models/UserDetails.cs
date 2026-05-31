namespace Automaton.Studio.Server.Models;

public class UserDetails
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool EmailConfirmed { get; set; }

    public string? PublicUsername { get; set; }

    public DateTime? RegisteredAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    public string? RegistrationProvider { get; set; }

    public bool IsLocked { get; set; }

    public int AccessFailedCount { get; set; }

    public int AppCount { get; set; }

    public int RunnerCount { get; set; }

    public ICollection<UserAppLink> Apps { get; set; } = new List<UserAppLink>();

    public ICollection<UserRunnerLink> Runners { get; set; } = new List<UserRunnerLink>();

    /// <summary>
    /// User's system roles (e.g., "Admin", "SysAdmin", "User")
    /// </summary>
    public ICollection<string> Roles { get; set; } = new List<string>();
}

public class UserAppLink
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class UserRunnerLink
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime? LastHeartbeatAtUtc { get; set; }

    public DateTime? LastConnectedAtUtc { get; set; }

    public string? Version { get; set; }
}
