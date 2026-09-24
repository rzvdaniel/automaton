using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.StaticFiles;
using Automaton.Runner.Console.Models;
using Automaton.Core.Models;
using Automaton.Core.Constants;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubFileTransferService
{
    private const string FileUploadCompleteMethod = "FileUploadComplete";
    private const string FileUploadFailedMethod = "FileUploadFailed";
    private const string FileChunkReadyMethod = "FileChunkReady";
    private const string ReceiveFileChunkMethod = "ReceiveFileChunk";
    private const string FetchFileChunkMethod = "FetchFileChunk";
    private const string GetAppMediaStatsMethod = "GetAppMediaStats";
    private const string GetAppMediaFileMetadataMethod = "GetAppMediaFileMetadata";
    private static readonly FileExtensionContentTypeProvider contentTypeProvider = new();
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly ILogger logger;
    private HubConnection? connection;

    public HubFileTransferService(RunnerWorkspaceService runnerWorkspaceService, ILogger logger)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.logger = logger;
    }

    public void RegisterHandlers(HubConnection current)
    {
        connection = current;
        current.On<FileUploadChunk>(ReceiveFileChunkMethod, HandleReceiveFileChunkAsync);
        current.On<FetchFileChunkRequest>(FetchFileChunkMethod, HandleFetchFileChunkAsync);
        current.On<string, AppMediaStatsResponse>(GetAppMediaStatsMethod, GetAppMediaStats);
        current.On<string, string, AppMediaFileMetadataResponse>(GetAppMediaFileMetadataMethod, GetAppMediaFileMetadata);
    }

    private static Guid ParseAppId(string appId)
    {
        if (!Guid.TryParse(appId, out var parsed))
            throw new ArgumentException("Invalid app ID.");
        return parsed;
    }
    private async Task HandleReceiveFileChunkAsync(FileUploadChunk chunk)
    {
        try
        {
            var filePath = string.Equals(chunk.StorageScope, FileTransferStorageScopes.Media, StringComparison.Ordinal)
                ? ResolveAppMediaUploadPath(chunk)
                : ResolveTempUploadPath(chunk);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await using var fs = new FileStream(filePath,
                chunk.ChunkIndex == 0 ? FileMode.Create : FileMode.Append,
                FileAccess.Write, FileShare.None);
            await fs.WriteAsync(chunk.Data);

            logger.Debug("Upload chunk {Index} written: SessionId={SessionId} IsLast={IsLast}",
                chunk.ChunkIndex, chunk.SessionId, chunk.IsLast);

            if (chunk.IsLast)
            {
                logger.Debug("Upload complete: SessionId={SessionId} Path={Path}", chunk.SessionId, filePath);
                await connection!.SendAsync(FileUploadCompleteMethod, new FileUploadCompleteMessage
                {
                    SessionId = chunk.SessionId,
                    LocalPath = filePath
                });
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to handle upload chunk: SessionId={SessionId}", chunk.SessionId);
            try
            {
                await connection!.SendAsync(FileUploadFailedMethod, new FileUploadFailedMessage
                {
                    SessionId = chunk.SessionId,
                    Error = $"Runner failed to write upload chunk: {ex.Message}"
                });
            }
            catch (Exception notifyEx)
            {
                logger.Error(notifyEx, "Failed to notify server of upload failure: SessionId={SessionId}", chunk.SessionId);
            }
        }
    }

    private async Task HandleFetchFileChunkAsync(FetchFileChunkRequest request)
    {
        try
        {
            string filePath;
            string fileName;
            string contentType;

            if (string.Equals(request.StorageScope, FileTransferStorageScopes.Media, StringComparison.Ordinal))
            {
                if (!request.AppId.HasValue)
                {
                    await SendFileChunkErrorAsync(request.SessionId, 400, "App ID is required.");
                    return;
                }

                filePath = ResolveAppMediaFilePath(request.AppId.Value, request.RelativePath);
                if (!File.Exists(filePath))
                {
                    await SendFileChunkErrorAsync(request.SessionId, 404, "File not found.");
                    return;
                }

                fileName = Path.GetFileName(filePath);
                contentType = GetContentType(fileName);
            }
            else
            {
                var dir = Path.Combine(Path.GetTempPath(), "results", request.SessionId);
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir) : [];

                if (files.Length == 0)
                {
                    logger.Warning("No result file found for download: SessionId={SessionId}", request.SessionId);
                    await connection!.SendAsync(FileChunkReadyMethod, new FileChunkReadyMessage
                    {
                        SessionId = request.SessionId,
                        Data = [],
                        IsLast = true,
                        FileName = "error",
                        ContentType = "application/octet-stream"
                    });
                    return;
                }

                filePath = files[0];
                fileName = Path.GetFileName(filePath);
                contentType = GetContentType(fileName);
            }

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(request.Offset, SeekOrigin.Begin);

            var buffer = new byte[request.ChunkSize];
            var bytesRead = await fs.ReadAsync(buffer);
            var isLast = fs.Position >= fs.Length;
            var data = bytesRead == request.ChunkSize ? buffer : buffer[..bytesRead];

            logger.Debug("Download chunk sent: SessionId={SessionId} Offset={Offset} Bytes={Bytes} IsLast={IsLast}",
                request.SessionId, request.Offset, bytesRead, isLast);

            await connection!.SendAsync(FileChunkReadyMethod, new FileChunkReadyMessage
            {
                SessionId = request.SessionId,
                Data = data,
                IsLast = isLast,
                FileName = fileName,
                ContentType = contentType
            });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to handle download chunk request: SessionId={SessionId}", request.SessionId);
            await SendFileChunkErrorAsync(request.SessionId, 500, ex.Message);
        }
    }

    private static string ResolveTempUploadPath(FileUploadChunk chunk)
    {
        var dir = Path.Combine(Path.GetTempPath(), "uploads", chunk.SessionId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, chunk.FileName);
    }

    private string ResolveAppMediaUploadPath(FileUploadChunk chunk)
    {
        if (!chunk.AppId.HasValue)
        {
            throw new ArgumentException("App ID is required for media uploads.");
        }

        return runnerWorkspaceService.ResolveAppMediaFilePath(chunk.AppId.Value, chunk.RelativePath);
    }

    private string GetAppMediaDirectory(Guid appId)
    {
        return Path.GetFullPath(Path.Combine(runnerWorkspaceService.GetLegacyControllerDataDirectory(appId), "media"));
    }

    private string ResolveAppMediaFilePath(Guid appId, string relativePath)
    {
        return runnerWorkspaceService.ResolveAppMediaFilePath(appId, relativePath);
    }

    private AppMediaStatsResponse GetAppMediaStats(string appId)
    {
        try
        {
            var mediaDir = GetAppMediaDirectory(ParseAppId(appId));
            if (!Directory.Exists(mediaDir))
            {
                return new AppMediaStatsResponse { Success = true };
            }

            var files = Directory.EnumerateFiles(mediaDir, "*", SearchOption.AllDirectories).ToList();
            return new AppMediaStatsResponse
            {
                Success = true,
                FileCount = files.Count,
                TotalBytes = files.Sum(path => new FileInfo(path).Length)
            };
        }
        catch (Exception ex)
        {
            return new AppMediaStatsResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private AppMediaFileMetadataResponse GetAppMediaFileMetadata(string appId, string relativePath)
    {
        try
        {
            var filePath = runnerWorkspaceService.ResolveAppMediaFilePath(ParseAppId(appId), relativePath);
            if (!File.Exists(filePath))
            {
                return new AppMediaFileMetadataResponse
                {
                    Success = false,
                    StatusCode = 404,
                    Error = "File not found."
                };
            }

            var info = new FileInfo(filePath);
            return new AppMediaFileMetadataResponse
            {
                Success = true,
                StatusCode = 200,
                FileName = info.Name,
                ContentType = GetContentType(info.Name),
                Length = info.Length
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = 403,
                Error = ex.Message
            };
        }
        catch (ArgumentException ex)
        {
            return new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = 400,
                Error = ex.Message
            };
        }
        catch (Exception ex)
        {
            return new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = 500,
                Error = ex.Message
            };
        }
    }

    private async Task SendFileChunkErrorAsync(string sessionId, int statusCode, string error)
    {
        await connection!.SendAsync(FileChunkReadyMethod, new FileChunkReadyMessage
        {
            SessionId = sessionId,
            StatusCode = statusCode,
            Error = error,
            Data = [],
            IsLast = true,
            FileName = "error",
            ContentType = "application/octet-stream"
        });
    }

    private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private static string GetContentType(string fileName)
        => contentTypeProvider.TryGetContentType(fileName, out var contentType)
            ? contentType
            : "application/octet-stream";
}