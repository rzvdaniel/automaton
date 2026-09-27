namespace Automaton.Api.Services;

/// <summary>
/// Enforces a combined platform storage budget for published apps and their staging files.
/// </summary>
internal sealed class PublishedAppStorageBudget
{
    private readonly long maximumBytes;
    private readonly int maximumFiles;

    public PublishedAppStorageBudget(long maximumBytes, int maximumFiles)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Published storage capacity must be positive.");
        if (maximumFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFiles), "Published file capacity must be positive.");

        this.maximumBytes = maximumBytes;
        this.maximumFiles = maximumFiles;
    }

    public void ValidateWrite(string rootPath, string filePath, long replacementBytes)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath);
        AppArchiveStorageBounds.ValidateFileWrite(
            rootPath,
            relativePath,
            replacementBytes,
            maximumBytes,
            maximumFiles);
    }

    public void ValidateRoot(string rootPath) =>
        AppArchiveStorageBounds.ValidateDirectory(rootPath, maximumBytes, maximumFiles);
}
