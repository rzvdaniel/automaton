using System.Text.Json;

using Automaton.Api.Models;

namespace Automaton.Api.Services;

/// <summary>
/// Normalizes captured backend output into the RuntimeLogEvent contract used by Studio.
/// Today this parses the stored controller response payload. Later, when Studio
/// supports live runner streaming or long-running service backends, the same DTO
/// shape can be populated directly from runner events without changing the UI.
/// </summary>
public static class AppRuntimeLogEventParser
{
    public static List<AppRuntimeLogEventDto> Parse(string? responsePayload, DateTime fallbackTimestamp)
    {
        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(responsePayload);
            if (!document.RootElement.TryGetProperty("logs", out var logsElement) || logsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var events = new List<AppRuntimeLogEventDto>();
            foreach (var item in logsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var line = item.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                events.Add(new AppRuntimeLogEventDto
                {
                    Timestamp = fallbackTimestamp,
                    Level = InferLevel(line),
                    Message = line,
                    Source = AppRuntimeLogSources.Backend,
                    Stream = InferStream(line),
                });
            }

            return events;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string InferLevel(string line)
    {
        return line.StartsWith("traceback", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("error", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("exception", StringComparison.OrdinalIgnoreCase)
            ? AppRuntimeLogLevels.Error
            : AppRuntimeLogLevels.Info;
    }

    private static string InferStream(string line)
    {
        return InferLevel(line) == AppRuntimeLogLevels.Error
            ? AppRuntimeLogStreams.Stderr
            : AppRuntimeLogStreams.Stdout;
    }
}
