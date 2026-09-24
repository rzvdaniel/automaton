using System.Text.RegularExpressions;

namespace Automaton.Ai.Providers;

internal static class OpenAiCompatibleRateLimits
{
    private static readonly Regex RetryAfterPattern = new(
        @"retry[- ](?:after|in)\D*(?<secs>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsRateLimitException(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("quota_exceeded", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exceeded_current_quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("suspended", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("rate limited", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseRetryAfter(Exception? exception, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (exception is null)
            return false;
        var match = RetryAfterPattern.Match(exception.Message);
        if (!match.Success || !int.TryParse(match.Groups["secs"].Value, out var seconds) || seconds <= 0)
            return false;
        retryAfter = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
