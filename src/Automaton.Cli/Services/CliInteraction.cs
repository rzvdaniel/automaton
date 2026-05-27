namespace Automaton.Cli.Services;

public static class CliInteraction
{
    public static bool IsNonInteractive(bool explicitNonInteractive, bool asJson = false)
    {
        if (asJson || explicitNonInteractive)
        {
            return true;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("AUTOMATON_CI")))
        {
            return true;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("CI")))
        {
            return true;
        }

        return Console.IsOutputRedirected || Console.IsInputRedirected;
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
