namespace Automaton.Studio.Server.Exceptions;

/// <summary>
/// Thrown when a user's subscription plan does not permit a specific feature.
/// Middleware converts this to HTTP 402 with a structured JSON body.
/// </summary>
public class PlanLimitException : Exception
{
    /// <summary>The feature the user tried to access (e.g. "CanCreateProjects").</summary>
    public string FeatureName { get; }

    /// <summary>The minimum plan name that unlocks this feature (e.g. "Starter").</summary>
    public string RequiredPlan { get; }

    public PlanLimitException(string featureName, string requiredPlan)
        : base($"Feature '{featureName}' requires the '{requiredPlan}' plan or higher.")
    {
        FeatureName  = featureName;
        RequiredPlan = requiredPlan;
    }
}
