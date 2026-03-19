using System.Text;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Builds a compact App Memory Block summarising what has been built and what
/// sessions have been worked on. Injected into the system prompt whenever the AI
/// starts a new session for an app that already exists, so it always knows it is
/// continuing existing work — not starting from scratch.
/// </summary>
public class AppMemoryService
{
    // ~600 tokens budget for the memory block — enough to be useful, cheap enough to always send.
    private const int MaxChars = 2400;

    private readonly ApplicationDbContext _db;
    private readonly IAiChatHistoryService _chatHistory;

    public AppMemoryService(ApplicationDbContext db, IAiChatHistoryService chatHistory)
    {
        _db          = db;
        _chatHistory = chatHistory;
    }

    /// <summary>
    /// Returns an App Memory Block to inject into the system prompt, or null if the
    /// app has no prior history (i.e. this is genuinely the first session).
    /// </summary>
    public async Task<string?> BuildMemoryBlockAsync(Guid appId, Guid currentSessionId, Guid userId)
    {
        // Collect the last N versions (most recent first) — each has an AI-generated summary
        var recentVersions = await _db.AppVersions
            .Where(v => v.AppId == appId)
            .OrderByDescending(v => v.VersionNumber)
            .Take(10)
            .Select(v => new { v.VersionNumber, v.Summary, v.Trigger, v.CreatedAt })
            .ToListAsync();

        // Collect past session names (exclude the current one — it's brand new and empty)
        var pastSessions = await _db.AiChatSessions
            .Where(s => s.ResourceType == ChatResourceType.WebApp &&
                        s.ResourceId == appId &&
                        s.Id != currentSessionId &&
                        s.AiChatSessionUsers.Any(u => u.UserId == userId))
            .OrderByDescending(s => s.UpdatedAt)
            .Take(10)
            .Select(s => new { s.Title, s.UpdatedAt })
            .ToListAsync();

        if (recentVersions.Count == 0 && pastSessions.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("## App Memory");
        sb.AppendLine();
        sb.AppendLine("This app already exists. You are continuing work that has been done in previous sessions.");
        sb.AppendLine("Read the existing files before making any changes. Make ONLY the changes the user requests — do not rewrite or replace files that are already correct.");
        sb.AppendLine();

        if (recentVersions.Count > 0)
        {
            sb.AppendLine("### What has been built (version history)");
            sb.AppendLine();
            foreach (var v in recentVersions)
            {
                var summary = ShortenSummary(v.Summary, 200);
                sb.AppendLine($"- **v{v.VersionNumber}** ({v.Trigger}, {v.CreatedAt:MMM d}): {summary}");
            }
            sb.AppendLine();
        }

        if (pastSessions.Count > 0)
        {
            sb.AppendLine("### Previous work sessions");
            sb.AppendLine();
            foreach (var s in pastSessions)
            {
                sb.AppendLine($"- {s.Title} ({s.UpdatedAt:MMM d})");
            }
            sb.AppendLine();
        }

        var result = sb.ToString().Trim();

        // Hard cap — truncate if unusually large
        if (result.Length > MaxChars)
            result = result[..MaxChars] + "\n_(truncated)_";

        return result;
    }

    private static string ShortenSummary(string? summary, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return "(no summary)";

        // If it looks like JSON, extract just the overview field
        if (summary.TrimStart().StartsWith('{'))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(summary);
                if (doc.RootElement.TryGetProperty("overview", out var overview))
                    summary = overview.GetString() ?? summary;
            }
            catch { /* fall through to raw truncation */ }
        }

        summary = summary.Trim();
        return summary.Length <= maxLength ? summary : summary[..maxLength] + "…";
    }
}
