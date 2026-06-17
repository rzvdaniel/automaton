namespace Automaton.Api.Config;

public class UserLockoutConfig
{
    public bool AllowedForNewUsers { get; set; } = true;

    public int MaxFailedAccessAttempts { get; set; } = 5;

    public int DefaultLockoutTimeSpanMinutes { get; set; } = 15;
}
