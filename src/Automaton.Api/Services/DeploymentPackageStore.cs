using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

public sealed class DeploymentPackageStore(IWebHostEnvironment webHostEnvironment)
{
    public async Task<string> StoreAsync(Guid operationId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > AppArchiveStorageBounds.MaximumUploadBytes)
            throw new InvalidDataException($"Deployment package exceeds the {AppArchiveStorageBounds.MaximumUploadBytes}-byte upload limit.");

        var directory = GetPackageDirectory();
        Directory.CreateDirectory(directory);
        var packagePath = Path.Combine(directory, $"{operationId:N}.zip");

        try
        {
            await using var package = new FileStream(
                packagePath,
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
            return packagePath;
        }
        catch
        {
            TryDelete(packagePath);
            throw;
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

    private string GetPackageDirectory()
    {
        return Path.Combine(webHostEnvironment.ContentRootPath, "AppData", "deployment-operations");
    }
}
