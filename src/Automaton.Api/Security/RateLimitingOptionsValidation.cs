using Microsoft.Extensions.Options;

namespace Automaton.Api.Security;

public class RateLimitingOptionsValidation : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var errors = new List<string>();

        ValidatePolicy(options.GlobalAnonymous, nameof(options.GlobalAnonymous), errors);
        ValidatePolicy(options.GlobalAuthenticated, nameof(options.GlobalAuthenticated), errors);
        ValidatePolicy(options.AuthLogin, nameof(options.AuthLogin), errors);
        ValidatePolicy(options.AuthRegistration, nameof(options.AuthRegistration), errors);
        ValidatePolicy(options.AuthForgotPassword, nameof(options.AuthForgotPassword), errors);
        ValidatePolicy(options.AuthResendConfirmation, nameof(options.AuthResendConfirmation), errors);
        ValidatePolicy(options.AuthResetPassword, nameof(options.AuthResetPassword), errors);
        ValidatePolicy(options.AuthRefreshToken, nameof(options.AuthRefreshToken), errors);
        ValidatePolicy(options.ApiKeyValidation, nameof(options.ApiKeyValidation), errors);
        ValidatePolicy(options.PublicAppControllerExecution, nameof(options.PublicAppControllerExecution), errors);
        ValidatePolicy(options.PublicAppFlowExecution, nameof(options.PublicAppFlowExecution), errors);
        ValidatePolicy(options.PublicAppStepExecution, nameof(options.PublicAppStepExecution), errors);

        if (options.MaxConcurrentMultipartRequests <= 0)
        {
            errors.Add($"{nameof(options.MaxConcurrentMultipartRequests)} must be greater than 0.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidatePolicy(RateLimitPolicyOptions? policy, string policyName, List<string> errors)
    {
        if (policy is null)
        {
            errors.Add($"{policyName} must be configured.");
            return;
        }

        if (policy.PermitLimit <= 0)
        {
            errors.Add($"{policyName}.PermitLimit must be greater than 0.");
        }

        if (policy.WindowSeconds <= 0)
        {
            errors.Add($"{policyName}.WindowSeconds must be greater than 0.");
        }
    }
}
