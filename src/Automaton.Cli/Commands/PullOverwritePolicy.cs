using Automaton.Cli.Commands;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class PullOverwritePolicy
{
    private const int PreviewCount = 10;    internal static List<string> GetOverwritePaths(IEnumerable<PullCommand.SourceDownload> downloads)
    {
        var overwritePaths = new List<string>();

        foreach (var download in downloads)
        {
            if (File.Exists(download.LocalPath))
            {
                overwritePaths.Add(download.RemotePath);
            }
        }

        return overwritePaths;
    }

    internal static void EnsureOverwriteApproved(IReadOnlyList<string> overwritePaths, bool force, bool nonInteractive)
    {
        if (overwritePaths.Count == 0)
        {
            return;
        }

        if (force)
        {
            WriteOverwriteNotice(overwritePaths, nonInteractive, forced: true);
            return;
        }

        if (nonInteractive)
        {
            throw new InvalidOperationException(BuildNonInteractiveOverwriteError(overwritePaths));
        }

        WriteOverwriteNotice(overwritePaths, nonInteractive, forced: false);
        Console.Write("Continue? [y/N] ");
        var confirmation = Console.ReadLine()?.Trim();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationCanceledException("Pull cancelled.");
        }
    }

    private static void WriteOverwriteNotice(IReadOnlyList<string> overwritePaths, bool nonInteractive, bool forced)
    {
        var message = BuildOverwriteMessage(overwritePaths, forced);

        if (nonInteractive)
        {
            Console.WriteLine(message);
            return;
        }

        foreach (var line in message.Split(Environment.NewLine))
        {
            AnsiConsole.MarkupLine(Markup.Escape(line));
        }
    }

    private static string BuildNonInteractiveOverwriteError(IReadOnlyList<string> overwritePaths)
    {
        var preview = BuildOverwritePreview(overwritePaths);
        return $"Pull would overwrite {overwritePaths.Count} existing local file(s). Re-run with --force to continue.{Environment.NewLine}{preview}";
    }

    private static string BuildOverwriteMessage(IReadOnlyList<string> overwritePaths, bool forced)
    {
        var intro = forced
            ? $"Warning: --force enabled. Pull will overwrite {overwritePaths.Count} existing local file(s) from the remote app."
            : $"Warning: pull will overwrite {overwritePaths.Count} existing local file(s) from the remote app.";

        return $"{intro}{Environment.NewLine}{BuildOverwritePreview(overwritePaths)}{Environment.NewLine}Local-only files will be kept.";
    }

    private static string BuildOverwritePreview(IReadOnlyList<string> overwritePaths)
    {
        var lines = overwritePaths
            .Take(PreviewCount)
            .Select(path => $"  {path}")
            .ToList();

        if (overwritePaths.Count > PreviewCount)
        {
            lines.Add($"  ... and {overwritePaths.Count - PreviewCount} more");
        }

        return string.Join(Environment.NewLine, lines);
    }


}
