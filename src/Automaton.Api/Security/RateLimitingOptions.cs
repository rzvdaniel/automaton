namespace Automaton.Api.Security;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitPolicyOptions GlobalAnonymous { get; set; } = new(200, 60);
    public RateLimitPolicyOptions RunnerApi { get; set; } = new(2000, 60);
    public RateLimitPolicyOptions GlobalAuthenticated { get; set; } = new(1000, 60);
    /// <summary>Static files (scripts, styles, images, fonts) of an app, per visitor address; a page loads dozens at once.</summary>
    public RateLimitPolicyOptions AppStaticAssets { get; set; } = new(3000, 60);
    public int MaxConcurrentMultipartRequests { get; set; } = 2;
    public RateLimitPolicyOptions AuthLogin { get; set; } = new(5, 60);
    public RateLimitPolicyOptions AuthRegistration { get; set; } = new(3, 3600);
    public RateLimitPolicyOptions AuthForgotPassword { get; set; } = new(5, 3600);
    public RateLimitPolicyOptions AuthResendConfirmation { get; set; } = new(3, 3600);
    public RateLimitPolicyOptions AuthResetPassword { get; set; } = new(5, 3600);
    public RateLimitPolicyOptions AuthRefreshToken { get; set; } = new(30, 300);
    /// <summary>Viewing and accepting an invitation, per address.</summary>
    public RateLimitPolicyOptions AuthInviteAccept { get; set; } = new(10, 600);
    /// <summary>Wrong API keys per address; past the limit the address gets 429 until the window ends (accepted keys are not affected).</summary>
    public RateLimitPolicyOptions ApiKeyFailures { get; set; } = new(20, 60);
    public RateLimitPolicyOptions ApiKeyValidation { get; set; } = new(20, 60);
    public RateLimitPolicyOptions PublicAppControllerExecution { get; set; } = new(10, 60);
    public RateLimitPolicyOptions PublicAppFlowExecution { get; set; } = new(10, 60);
    public RateLimitPolicyOptions PublicAppStepExecution { get; set; } = new(10, 60);
    /// <summary>Calls to Action webhooks per caller address, secret or not; each webhook also allows a number of accepted calls a minute.</summary>
    public RateLimitPolicyOptions ActionWebhookCall { get; set; } = new(120, 60);
    public RateLimitPolicyOptions PublicMarketplaceAppLaunch { get; set; } = new(1, 600);
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
