using System.Security.Cryptography;
using System.Text;

namespace Automaton.Runner.Console.Services;

internal static class AppActionSourceFiles
{
    public static void CopyToRuntime(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            if (ContainsGeneratedRuntimeSegment(relativePath)) continue;
            var target = Path.Combine(destination, relativePath);
            EnsureInside(destination, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public static string ComputeContentHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(path => !ContainsGeneratedRuntimeSegment(Path.GetRelativePath(directory, path)))
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData([0]);
            using var stream = File.OpenRead(file);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, read);
        }
        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    public static void EnsureInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Action files must stay inside the Action folder.");
    }

    private static bool ContainsGeneratedRuntimeSegment(string relativePath)
        => relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(segment => segment is ".venv" or "__pycache__");
}
