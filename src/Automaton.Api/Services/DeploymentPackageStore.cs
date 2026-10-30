using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Automaton.Api.Config;
using Automaton.Core.Models;
using System.Security.Cryptography;

namespace Automaton.Api.Services;

public sealed class DeploymentPackageStore(
    IWebHostEnvironment webHostEnvironment,
    IOptions<DeploymentPackageStorageOptions> storageOptions)
{
    private static readonly SemaphoreSlim StorageGate = new(1, 1);
    private readonly DeploymentPackageStorageOptions options = storageOptions.Value;

    public async Task<string> StoreAsync(Guid operationId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > AppArchiveStorageBounds.MaximumUploadBytes)
            throw new InvalidDataException($"Deployment package exceeds the {AppArchiveStorageBounds.MaximumUploadBytes}-byte upload limit.");

        var directory = GetPackageDirectory();
        Directory.CreateDirectory(directory);
        var packagePath = Path.Combine(directory, $"{operationId:N}.zip");
        var stagingPath = Path.Combine(directory, $"{operationId:N}.{Guid.NewGuid():N}.uploading");

        await StorageGate.WaitAsync(cancellationToken);
        try
        {
            await using var storageLock = await OpenStorageLockAsync(directory, cancellationToken);
            EnsureAvailableCapacity(directory, file.Length);
            await WriteStagedPackageAsync(file, stagingPath, cancellationToken);
            File.Move(stagingPath, packagePath);
            return packagePath;
        }
        catch
        {
            TryDelete(stagingPath);
            throw;
        }
        finally
        {
            StorageGate.Release();
        }
    }

    public static FileStream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
    }

    public async Task<string> StoreRunnerTransferAsync(
        Guid operationId,
        bool input,
        Stream source,
        long length,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || length < 0 || length > RunnerFileTransferBounds.MaximumBytes)
            throw new InvalidDataException($"Runner transfers are limited to {RunnerFileTransferBounds.MaximumBytes} bytes.");

        var directory = GetPackageDirectory();
        Directory.CreateDirectory(directory);
        var path = GetRunnerTransferPath(operationId, input);
        var stagingPath = Path.Combine(directory, $"{operationId:N}.{Guid.NewGuid():N}.uploading");
        await StorageGate.WaitAsync(cancellationToken);
        try
        {
            await using var storageLock = await OpenStorageLockAsync(directory, cancellationToken);
            if (File.Exists(path))
                return path;

            EnsureAvailableCapacity(directory, length);
            await using (var destination = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, useAsync: true))
            {
                await AppArchiveStorageBounds.CopyExactlyAsync(
                    source, destination, length, RunnerFileTransferBounds.MaximumBytes, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
            File.Move(stagingPath, path);
            return path;
        }
        catch
        {
            TryDelete(stagingPath);
            throw;
        }
        finally
        {
            StorageGate.Release();
        }
    }

    public FileStream OpenRunnerTransfer(Guid operationId, bool input)
        => OpenRead(GetRunnerTransferPath(operationId, input));

    public string GetRunnerTransferPath(Guid operationId, bool input)
        => Path.Combine(GetPackageDirectory(), $"{operationId:N}.{(input ? "transfer-in" : "transfer-out")}");

    public async Task TryDeleteRunnerTransferAsync(Guid operationId, bool input)
    {
        await StorageGate.WaitAsync();
        try
        {
            var directory = GetPackageDirectory();
            if (!Directory.Exists(directory))
                return;

            await using var storageLock = await OpenStorageLockAsync(directory, CancellationToken.None);
            TryDelete(GetRunnerTransferPath(operationId, input));
        }
        catch
        {
            // A failed cleanup must not change the completed transfer result.
        }
        finally
        {
            StorageGate.Release();
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Package cleanup is best effort and must not change operation state.
        }
    }

    public async Task<int> CleanupUnreferencedPackagesAsync(
        IReadOnlyCollection<string> activePackagePaths,
        TimeSpan minimumFileAge,
        CancellationToken cancellationToken)
    {
        var directory = GetPackageDirectory();
        if (!Directory.Exists(directory))
            return 0;

        await StorageGate.WaitAsync(cancellationToken);
        try
        {
            await using var storageLock = await OpenStorageLockAsync(directory, cancellationToken);
            return DeleteExpiredUnreferencedPackages(directory, activePackagePaths, minimumFileAge, cancellationToken);
        }
        finally
        {
            StorageGate.Release();
        }
    }

    private string GetPackageDirectory()
    {
        return Path.Combine(webHostEnvironment.ContentRootPath, "AppData", "deployment-operations");
    }

    private static async Task<FileStream> OpenStorageLockAsync(string directory, CancellationToken cancellationToken)
    {
        var appDataDirectory = Directory.GetParent(directory)?.FullName
            ?? throw new InvalidOperationException("Deployment package storage must have a parent directory.");
        Directory.CreateDirectory(appDataDirectory);
        var lockPath = Path.Combine(appDataDirectory, ".deployment-operations.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 1, useAsync: true);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private void EnsureAvailableCapacity(string directory, long incomingBytes)
    {
        long storedBytes = 0;
        var storedFiles = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            storedFiles++;
            var fileLength = new FileInfo(path).Length;
            if (fileLength > options.MaximumStoredBytes - storedBytes)
                throw CreateStorageLimitException();
            storedBytes += fileLength;
        }

        if (storedFiles >= options.MaximumStoredPackages ||
            incomingBytes > options.MaximumStoredBytes - storedBytes)
            throw CreateStorageLimitException();
    }

    private InvalidDataException CreateStorageLimitException() =>
        new($"Deployment package storage is full (limit: {options.MaximumStoredBytes} bytes across {options.MaximumStoredPackages} files).");

    private static int DeleteExpiredUnreferencedPackages(
        string directory,
        IReadOnlyCollection<string> activePackagePaths,
        TimeSpan minimumFileAge,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var activePaths = activePackagePaths.Select(Path.GetFullPath).ToHashSet(comparer);
        var cutoff = DateTime.UtcNow - minimumFileAge;
        var deletedCount = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            var name = Path.GetFileName(fullPath);
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".uploading", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".transfer-in", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".transfer-out", StringComparison.OrdinalIgnoreCase))
                continue;

            if (activePaths.Contains(fullPath) || File.GetLastWriteTimeUtc(fullPath) > cutoff)
                continue;

            File.Delete(fullPath);
            deletedCount++;
        }

        return deletedCount;
    }

    private static async Task WriteStagedPackageAsync(
        IFormFile file,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        await using var package = new FileStream(
            stagingPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            useAsync: true);
        await using var input = file.OpenReadStream();
        await AppArchiveStorageBounds.CopyExactlyAsync(
            input,
            package,
            file.Length,
            AppArchiveStorageBounds.MaximumUploadBytes,
            cancellationToken);
        await package.FlushAsync(cancellationToken);
    }
}
