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
}

/// <summary>Runner sends to server when all upload chunks have been written to disk.</summary>
public class FileUploadCompleteMessage
{
    public string SessionId { get; set; } = string.Empty;
    /// <summary>Absolute path on the runner's filesystem where the upload was stored.</summary>
    public string LocalPath { get; set; } = string.Empty;
}

/// <summary>Server sends to runner to request a chunk for download.</summary>
public class FetchFileChunkRequest
{
    public string SessionId { get; set; } = string.Empty;
    public long Offset { get; set; }
    public int ChunkSize { get; set; }
}

/// <summary>Runner sends to server with file bytes for download.</summary>
public class FileChunkReadyMessage
{
    public string SessionId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public bool IsLast { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}
