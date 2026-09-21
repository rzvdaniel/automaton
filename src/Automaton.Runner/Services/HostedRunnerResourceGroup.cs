namespace Automaton.Runner.Console.Services;

internal static class HostedRunnerResourceGroup
{
    public static string GetCgroupParent(Guid hostedRunnerId)
    {
        if (hostedRunnerId == Guid.Empty)
            throw new ArgumentException("Hosted Runner identity is required.", nameof(hostedRunnerId));
        return $"automaton-hosted-runner-{hostedRunnerId:N}.slice";
    }
}
