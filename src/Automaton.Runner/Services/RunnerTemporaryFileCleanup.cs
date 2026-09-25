namespace Automaton.Runner.Console.Services;

internal static class RunnerTemporaryFileCleanup
{
    public static void RemoveStaleUploadAndResultDirectories()
    {
        foreach (var baseDirectory in new[]
        {
            Path.Combine(Path.GetTempPath(), "uploads"),
            Path.Combine(Path.GetTempPath(), "results")
        })
        {
            if (!Directory.Exists(baseDirectory)) continue;
            foreach (var directory in Directory.GetDirectories(baseDirectory))
            {
                try
                {
                    var created = Directory.GetCreationTimeUtc(directory);
                    if (DateTime.UtcNow - created > TimeSpan.FromHours(1))
                        Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // Cleanup is best effort; active upload/result directories may still be in use.
                }
            }
        }
    }
}
