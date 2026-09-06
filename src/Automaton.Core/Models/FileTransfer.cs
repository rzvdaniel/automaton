namespace Automaton.Core.Models;

/// <summary>Server sends to runner during upload. Runner appends Data to its local file.</summary>
public class FileUploadChunk
{
    public string SessionId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public byte[] Data { get; set; } = [];
    public bool IsLast { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public Guid? AppId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string StorageScope { get; set; } = string.Empty;
}

/// <summary>Runner sends to server when all upload chunks have been written to disk.</summary>
public class FileUploadCompleteMessage
{
    public string SessionId { get; set; } = string.Empty;
    /// <summary>Absolute path on the runner's filesystem where the upload was stored.</summary>
    public string LocalPath { get; set; } = string.Empty;
}

/// <summary>Runner sends to server when an upload chunk fails to be written to disk.</summary>
public class FileUploadFailedMessage
{
    public string SessionId { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

/// <summary>Server sends to runner to request a chunk for download.</summary>
public class FetchFileChunkRequest
{
    public string SessionId { get; set; } = string.Empty;
    public long Offset { get; set; }
    public int ChunkSize { get; set; }
    public Guid? AppId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string StorageScope { get; set; } = string.Empty;
}

/// <summary>Runner sends to server with file bytes for download.</summary>
public class FileChunkReadyMessage
{
    public string SessionId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public bool IsLast { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public int StatusCode { get; set; } = 200;
    public string? Error { get; set; }
}

public static class FileTransferStorageScopes
{
    public const string Media = "media";
}

public static class AppRunnerFileAreas
{
    public const string Backend = "backend";
    public const string Data = "data";
}

public class AppMediaStatsResponse
{
    public bool Success { get; set; }
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
    public string? Error { get; set; }
}

public class AppMediaFileMetadataResponse
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
    public string? Error { get; set; }
}
