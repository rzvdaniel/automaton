namespace Automaton.Runner.Console.Services;

public static class StorageMeasurement
{
    public static async Task<long> MeasureDirectoryAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return 0;
        return await Task.Run(() =>
        {
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) == 0) total = checked(total + info.Length);
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var info = new DirectoryInfo(child);
                    if ((info.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
            }
            return total;
        }, cancellationToken);
    }
}
