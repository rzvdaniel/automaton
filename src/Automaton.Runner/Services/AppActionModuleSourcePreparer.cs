using System.IO.Compression;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class AppActionModuleSourcePreparer
{
    public static async Task<AppActionDeploymentResult> PrepareAsync(
        PrepareAppActionModuleRequest request,
        CancellationToken cancellationToken,
        Func<Guid, Guid, DiscoveredAppAction, CancellationToken, Task<AppActionDeploymentResult>> prepare)
    {
        ValidateRequest(request);
        var root = CreateTemporaryRoot();
        try
        {
            ExtractSourceArchive(request.SourceZip, root);
            var action = ReadAction(request, root);
            return await prepare(request.AppId, request.DeploymentTargetId, action, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void ValidateRequest(PrepareAppActionModuleRequest request)
    {
        AppActionManifestParser.ValidateKey(request.ActionKey);
        if (request.SourceZip.Length == 0 || request.SourceZip.Length > 20 * 1024 * 1024)
            throw new InvalidOperationException("Action source package must be between 1 byte and 20 MB.");
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-prepare", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ExtractSourceArchive(byte[] sourceZip, string root)
    {
        using var stream = new MemoryStream(sourceZip, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ExtractArchiveEntries(archive, root);
    }

    private static void ExtractArchiveEntries(ZipArchive archive, string root)
    {
        if (archive.Entries.Count > 2_000) throw new InvalidOperationException("Action source package exceeds the entry limit.");
        var extractedBytes = 0L;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > 50L * 1024 * 1024) throw new InvalidOperationException("Action source package exceeds the extracted-size limit.");
            ExtractArchiveEntry(entry, root);
        }
    }

    private static void ExtractArchiveEntry(ZipArchiveEntry entry, string root)
    {
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixFileType is 0xA000 or 0x6000 or 0x2000)
            throw new InvalidOperationException("Action source package contains an unsupported link or device entry.");
        AppActionManifestParser.ValidateRelativePath(entry.FullName, "source path");
        var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
        AppActionSourceFiles.EnsureInside(root, target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, overwrite: true);
    }

    private static DiscoveredAppAction ReadAction(PrepareAppActionModuleRequest request, string root)
    {
        var manifestPath = Path.Combine(root, "action.yaml");
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("Action source package is missing action.yaml.");
        var manifest = AppActionManifestParser.ParseFile(manifestPath, request.ActionKey);
        var hash = AppActionSourceFiles.ComputeContentHash(root);
        if (!string.Equals(hash, request.ExpectedContentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Action source package hash did not match the expected hash.");
        return new DiscoveredAppAction(request.ActionKey, root, manifestPath, manifest);
    }

}
