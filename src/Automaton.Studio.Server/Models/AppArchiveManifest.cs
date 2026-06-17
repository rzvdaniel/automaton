namespace Automaton.Api.Models;

public class AppArchiveManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresAuthentication { get; set; } = true;
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class AppArchiveFileNames
{
    public const string Manifest = "app-manifest.json";
    public const string AutomatonConfig = "automaton.json";
    public const string Metadata = "_metadata.json";
    public const string VersionsFolder = "versions/";
    public const string BackendFolder = "backend/";
}

public class AppArchiveExportResult
{
    public required byte[] Content { get; set; }
    public required string FileName { get; set; }
}

public class AppArchiveImportResult
{
    public required AppModel App { get; set; }
    public int WrittenFileCount { get; set; }
}