namespace Automaton.Studio.Server.Security;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitPolicyOptions GlobalAnonymous { get; set; } = new(200, 60);
    public RateLimitPolicyOptions GlobalAuthenticated { get; set; } = new(1000, 60);
    public RateLimitPolicyOptions AuthLogin { get; set; } = new(5, 60);
    public RateLimitPolicyOptions AuthRegistration { get; set; } = new(3, 3600);
    public RateLimitPolicyOptions AuthForgotPassword { get; set; } = new(10, 3600);
    public RateLimitPolicyOptions AuthResetPassword { get; set; } = new(5, 3600);
    public RateLimitPolicyOptions AuthRefreshToken { get; set; } = new(30, 300);
    public RateLimitPolicyOptions ApiKeyValidation { get; set; } = new(20, 60);
    public RateLimitPolicyOptions PublicAppControllerExecution { get; set; } = new(10, 60);
    public RateLimitPolicyOptions PublicAppFlowExecution { get; set; } = new(10, 60);
    public RateLimitPolicyOptions PublicAppStepExecution { get; set; } = new(10, 60);
}

public class RateLimitPolicyOptions
{
    public RateLimitPolicyOptions()
    {
    }

    public RateLimitPolicyOptions(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
