using System.Text;
using Automaton.Mcp.Models;
using Automaton.Studio.Server.Config;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Compacts long conversation histories into a short rolling summary plus recent turns.
/// Reduces prompt size while preserving near-term execution context.
/// </summary>
public static class ConversationHistoryCompactor
{
    private const int DefaultMaxHistoryMessages = 30;

    public static List<ConversationMessage>? Compact(
        List<ConversationMessage>? history,
        int maxConversationHistoryMessages,
        HistoryCompactionConfig? settings = null)
    {
        if (history == null || history.Count == 0)
            return history;

        var maxHistory = maxConversationHistoryMessages > 0
            ? maxConversationHistoryMessages
            : DefaultMaxHistoryMessages;

        if (history.Count <= maxHistory)
            return history;

        // If the cap is extremely small, keep only the newest turns.
        if (maxHistory <= 2)
            return history.TakeLast(maxHistory).ToList();

        var recentMessages = ClampOrDefault(
            settings?.RecentMessages,
            HistoryCompactionDefaults.RecentMessages,
            HistoryCompactionBounds.MinRecentMessages,
            HistoryCompactionBounds.MaxRecentMessages);
        var maxSummaryEntries = ClampOrDefault(
            settings?.MaxSummaryEntries,
            HistoryCompactionDefaults.MaxSummaryEntries,
            HistoryCompactionBounds.MinMaxSummaryEntries,
            HistoryCompactionBounds.MaxMaxSummaryEntries);
        var maxSummaryChars = ClampOrDefault(
            settings?.MaxSummaryChars,
            HistoryCompactionDefaults.MaxSummaryChars,
            HistoryCompactionBounds.MinMaxSummaryChars,
            HistoryCompactionBounds.MaxMaxSummaryChars);
        var maxEntryChars = ClampOrDefault(
            settings?.MaxEntryChars,
            HistoryCompactionDefaults.MaxEntryChars,
            HistoryCompactionBounds.MinMaxEntryChars,
            HistoryCompactionBounds.MaxMaxEntryChars);

        var recentCount = Math.Min(recentMessages, maxHistory - 1);
        var olderCount = history.Count - recentCount;
        if (olderCount <= 0)
            return history.TakeLast(maxHistory).ToList();

        var older = history.Take(olderCount).ToList();
        var recent = history.Skip(olderCount).ToList();
        var summary = BuildSummary(older, maxSummaryEntries, maxSummaryChars, maxEntryChars);

        if (string.IsNullOrWhiteSpace(summary))
            return history.TakeLast(maxHistory).ToList();

        var summaryMessage = new ConversationMessage(
            "user",
            "Conversation summary of earlier turns (auto-generated for context compaction). " +
            "Treat this as authoritative background for decisions and completed work:\n\n" + summary);

        var compacted = new List<ConversationMessage> { summaryMessage };
        compacted.AddRange(recent);
        return compacted;
    }

    private static string BuildSummary(List<ConversationMessage> older, int maxSummaryEntries, int maxSummaryChars, int maxEntryChars)
    {
        var lines = new List<string>();

        foreach (var msg in older)
        {
            if (lines.Count >= maxSummaryEntries)
                break;

            var content = Normalize(msg.Content);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            if (content.Length > maxEntryChars)
                content = content[..maxEntryChars] + "...";

            var role = msg.Role?.ToLowerInvariant() switch
            {
                "assistant" => "Assistant",
                "user" => "User",
                "system" => "System",
                "tool" => "Tool",
                _ => "Message"
            };

            lines.Add($"- {role}: {content}");
        }

        if (lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length + line.Length + 1 > maxSummaryChars)
                break;

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(line);
        }

        return sb.ToString();
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(' ', text
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static int ClampOrDefault(int? value, int defaultValue, int min, int max)
    {
        if (!value.HasValue || value.Value <= 0)
            return defaultValue;

        return Math.Clamp(value.Value, min, max);
    }
}