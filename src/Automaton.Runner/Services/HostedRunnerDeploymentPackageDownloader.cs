using System.Security.Cryptography;
using System.Net;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerDeploymentPackageDownloader(
    RunnerWorkspaceService workspace,
    IHttpClientFactory httpClientFactory)
{
    private const long MaximumPackageBytes = 100L * 1024 * 1024;

    public async Task<string> DownloadAsync(
        HostedRunnerDeploymentCommand command,
        Uri apiUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var packageUrl = new Uri(command.PackageUrl, UriKind.Absolute);
        ValidateMetadata(command, packageUrl, apiUrl);
        using var client = httpClientFactory.CreateClient(nameof(HostedRunnerDeploymentPackageDownloader));
        client.BaseAddress = new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        using var response = await client.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new HttpRequestException("The staged package is unavailable on the active API host.",
                inner: null, statusCode: HttpStatusCode.NotFound);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength != command.PackageLength)
            throw new InvalidDataException("The downloaded deployment package length did not match its command.");

        var path = CreatePackagePath(command);
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await CopyAndVerifyAsync(input, path, command.PackageLength, command.PackageSha256, cancellationToken);
            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private string CreatePackagePath(HostedRunnerDeploymentCommand command)
    {
        var packageDirectory = Path.Combine(workspace.RunnerRoot, "package-transfer");
        Directory.CreateDirectory(packageDirectory);
        return Path.Combine(packageDirectory, $"{command.OperationId:N}-{command.ExecutionAttemptId:N}.zip");
    }

    private static async Task CopyAndVerifyAsync(
        Stream input,
        string path,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            received += read;
            if (received > expectedLength || received > MaximumPackageBytes)
                throw new InvalidDataException("The deployment package exceeded its declared size.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (received != expectedLength
            || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The deployment package checksum or size did not match its command.");
    }

    private static void ValidateMetadata(HostedRunnerDeploymentCommand command, Uri packageUrl, Uri apiUrl)
    {
        if (packageUrl.Scheme != Uri.UriSchemeHttps
            || !string.Equals(packageUrl.Host, apiUrl.Host, StringComparison.OrdinalIgnoreCase)
            || packageUrl.Port != apiUrl.Port)
            throw new InvalidDataException("The package URL must use HTTPS on the configured API host.");
        if (command.PackageLength <= 0 || command.PackageLength > MaximumPackageBytes
            || string.IsNullOrWhiteSpace(command.PackageSha256))
            throw new InvalidDataException("The deployment package metadata is invalid.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The partial transfer is not treated as a package and will be overwritten on retry.
        }
    }
}
