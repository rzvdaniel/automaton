namespace Automaton.Runner.Console.Services;

public static class StorageMeasurement
{
    public static async Task<long> MeasureDirectoryAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return 0;
        return await Task.Run(() => MeasureDirectory(root, cancellationToken), cancellationToken);
    }

    private static long MeasureDirectory(string root, CancellationToken cancellationToken)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total = checked(total + MeasureFiles(directory, cancellationToken));
            EnqueueDirectories(directory, pending);
        }
        return total;
    }

    private static long MeasureFiles(string directory, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0) total = checked(total + info.Length);
        }
        return total;
    }

    private static void EnqueueDirectories(string directory, Stack<string> pending)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var info = new DirectoryInfo(child);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(child);
        }
    }
}
