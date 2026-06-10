namespace Automaton.Studio.Server.Constants;

public static class RateLimitPolicyNames
{
    public const string AuthLogin = "AuthLogin";
    public const string AuthRegistration = "AuthRegistration";
    public const string AuthForgotPassword = "AuthForgotPassword";
    public const string AuthResendConfirmation = "AuthResendConfirmation";
    public const string AuthResetPassword = "AuthResetPassword";
    public const string AuthRefreshToken = "AuthRefreshToken";
    public const string ApiKeyValidation = "ApiKeyValidation";
    public const string PublicAppControllerExecution = "PublicAppControllerExecution";
    public const string PublicAppFlowExecution = "PublicAppFlowExecution";
    public const string PublicAppStepExecution = "PublicAppStepExecution";
    public const string PublicMarketplaceAppLaunch = "PublicMarketplaceAppLaunch";
}
