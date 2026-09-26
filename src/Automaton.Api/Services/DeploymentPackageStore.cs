using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Automaton.Api.Config;

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

    public FileStream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
    }

    public void TryDelete(string path)
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
            if (!Path.GetFileName(fullPath).EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(fullPath).EndsWith(".uploading", StringComparison.OrdinalIgnoreCase))
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
