using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Core.Models;
using Serilog;

namespace Automaton.Runner.Console.Services;

public sealed class AppActionDeploymentService
{
    private const string DeploymentMetadataFileName = ".automaton-action-deployment.json";
    private static readonly TimeSpan DependencyPreparationTimeout = TimeSpan.FromMinutes(10);
    private const int DefaultMaximumVersionsPerAction = 10;
    private const long DefaultMaximumVersionBytesPerAction = 1L * 1024 * 1024 * 1024;
    private readonly RunnerWorkspaceService workspace;
    private readonly AppActionExecutionCoordinator coordinator;
    private readonly ILogger logger = Log.ForContext<AppActionDeploymentService>();

    public AppActionDeploymentService(RunnerWorkspaceService workspace, AppActionExecutionCoordinator? coordinator = null)
    {
        this.workspace = workspace;
        this.coordinator = coordinator ?? new AppActionExecutionCoordinator();
    }

    public async Task<IReadOnlyList<AppActionDeploymentResult>> PrepareAllAsync(
        Guid appId,
        Guid deploymentTargetId,
        bool allowPrivilegedCapabilities = false,
        CancellationToken cancellationToken = default)
    {
        if (allowPrivilegedCapabilities && Automaton.Core.Enums.RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE")) != Automaton.Core.Enums.RunnerPurposes.Infrastructure)
            throw new UnauthorizedAccessException("Privileged Actions may only be prepared on Infrastructure Runners.");
        var actions = AppActionManifestParser.Discover(
            workspace.GetAppSourceRoot(appId, deploymentTargetId),
            allowPrivilegedCapabilities);
        var results = new List<AppActionDeploymentResult>(actions.Count);
        foreach (var action in actions)
        {
            results.Add(await PrepareAsync(appId, deploymentTargetId, action, cancellationToken));
        }

        return results;
    }

    public AppActionModulesResponse ListModules(Guid appId, Guid deploymentTargetId)
    {
        var actionsRoot = workspace.GetAppRuntimeActionsDirectory(appId, deploymentTargetId);
        if (!Directory.Exists(actionsRoot)) return new([]);
        var actions = new List<AppActionModuleInfo>();
        foreach (var directory in Directory.GetDirectories(actionsRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            var key = Path.GetFileName(directory);
            try
            {
                AppActionManifestParser.ValidateKey(key);
                var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, key);
                var manifestPath = Path.Combine(current, "action.yaml");
                if (!File.Exists(manifestPath)) continue;
                var manifest = AppActionManifestParser.ParseFile(manifestPath, key, allowPrivilegedCapabilities: true);
                actions.Add(new(key, manifest.Metadata.Name, manifest.Metadata.Description, manifest.Spec.Runtime,
                    manifest.Spec.Entrypoint, manifest.Spec.TimeoutSeconds, ReadCurrentHash(current),
                    manifest.Spec.Schedulable, manifest.Spec.AllowedExecutionScopes,
                    manifest.Spec.MinimumIntervalSeconds, manifest.Spec.Input));
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Ignoring invalid deployed Action directory {ActionDirectory} for app {AppId}", directory, appId);
            }
        }
        return new(actions);
    }

    public async Task<AppActionDeploymentResult> PrepareModuleAsync(PrepareAppActionModuleRequest request, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(request.ActionKey);
        if (request.SourceZip.Length == 0 || request.SourceZip.Length > 20 * 1024 * 1024)
            throw new InvalidOperationException("Action source package must be between 1 byte and 20 MB.");
        var runnerPurpose = Automaton.Core.Enums.RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        if (request.SystemInfrastructure && runnerPurpose != Automaton.Core.Enums.RunnerPurposes.Infrastructure)
            throw new UnauthorizedAccessException("System infrastructure Actions may only be prepared on Infrastructure Runners.");

        var temp = Path.Combine(Path.GetTempPath(), "automaton-action-prepare", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            using var stream = new MemoryStream(request.SourceZip, writable: false);
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            if (archive.Entries.Count > 2_000) throw new InvalidOperationException("Action source package exceeds the entry limit.");
            var extractedBytes = 0L;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixFileType is 0xA000 or 0x6000 or 0x2000)
                    throw new InvalidOperationException("Action source package contains an unsupported link or device entry.");
                extractedBytes = checked(extractedBytes + entry.Length);
                if (extractedBytes > 50L * 1024 * 1024) throw new InvalidOperationException("Action source package exceeds the extracted-size limit.");
                AppActionManifestParser.ValidateRelativePath(entry.FullName, "source path");
                var target = Path.GetFullPath(Path.Combine(temp, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                EnsureInside(temp, target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            var manifestPath = Path.Combine(temp, "action.yaml");
            if (!File.Exists(manifestPath)) throw new InvalidOperationException("Action source package is missing action.yaml.");
            var manifest = AppActionManifestParser.ParseFile(manifestPath, request.ActionKey, request.SystemInfrastructure);
            var action = new DiscoveredAppAction(request.ActionKey, temp, manifestPath, manifest);
            var hash = ComputeContentHash(temp);
            if (!string.Equals(hash, request.ExpectedContentHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Action source package hash did not match the expected hash.");
            return await PrepareAsync(
                request.AppId,
                request.DeploymentTargetId,
                action,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    public int SynchronizeModules(SynchronizeAppActionModulesRequest request)
    {
        var expected = request.ActionKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in expected) AppActionManifestParser.ValidateKey(key);
        var runtimeRoot = workspace.GetAppRuntimeActionsDirectory(
            request.AppId,
            request.DeploymentTargetId);
        if (!Directory.Exists(runtimeRoot)) return 0;
        var removed = 0;
        foreach (var directory in Directory.GetDirectories(runtimeRoot))
        {
            var key = Path.GetFileName(directory);
            if (expected.Contains(key)) continue;
            Directory.Delete(directory, recursive: true);
            removed++;
        }
        return removed;
    }

    public AppActionVersionsResponse ListVersions(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        var versions = new List<AppActionVersionInfo>();
        var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, actionKey);
        if (Directory.Exists(current))
            versions.Add(ToVersionInfo(current, "current", isCurrent: true));
        var versionsRoot = workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey);
        if (Directory.Exists(versionsRoot))
        {
            versions.AddRange(Directory.GetDirectories(versionsRoot)
                .Select(path => ToVersionInfo(path, Path.GetFileName(path), isCurrent: false))
                .OrderByDescending(version => version.CreatedAtUtc));
        }
        return new(versions);
    }

    public AppActionVersionOperationResponse RestoreVersion(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version)
    {
        ValidateVersionName(version);
        var versionsRoot = workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey);
        var selected = EnsureChildPath(versionsRoot, version);
        if (!Directory.Exists(selected)) return new(false, "Action version was not found.");
        var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, actionKey);
        var staging = Path.Combine(
            workspace.GetAppActionRuntimeRoot(appId, deploymentTargetId, actionKey),
            $".restore-{Guid.NewGuid():N}");
        CopyDirectory(selected, staging);
        ValidatePreparedDeployment(staging, actionKey);
        if (Directory.Exists(current))
        {
            var backup = Path.Combine(versionsRoot, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{HashSuffix(ReadCurrentHash(current))}");
            Directory.CreateDirectory(versionsRoot);
            Directory.Move(current, backup);
        }
        Directory.Move(staging, current);
        return new(true, $"Restored Action '{actionKey}' to version '{version}'.", ReadCurrentHash(current));
    }

    public AppActionVersionOperationResponse DeleteVersion(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version)
    {
        ValidateVersionName(version);
        var path = EnsureChildPath(
            workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey),
            version);
        if (!Directory.Exists(path)) return new(false, "Action version was not found.");
        Directory.Delete(path, recursive: true);
        return new(true, $"Deleted Action version '{version}'.");
    }

    private async Task<AppActionDeploymentResult> PrepareAsync(
        Guid appId,
        Guid deploymentTargetId,
        DiscoveredAppAction action,
        CancellationToken cancellationToken)
    {
        using var actionLock = await coordinator.EnterAsync(
            appId,
            deploymentTargetId,
            action.Key,
            cancellationToken);
        var contentHash = ComputeContentHash(action.DirectoryPath);
        var currentDirectory = workspace.GetAppActionCurrentDirectory(
            appId,
            deploymentTargetId,
            action.Key);
        if (string.Equals(ReadCurrentHash(currentDirectory), contentHash, StringComparison.Ordinal))
        {
            return new AppActionDeploymentResult
            {
                ActionKey = action.Key,
                Status = "unchanged",
                ContentHash = contentHash,
                Message = "Content hash matches the current deployment."
            };
        }

        var actionRoot = workspace.GetAppActionRuntimeRoot(appId, deploymentTargetId, action.Key);
        var stagingDirectory = Path.Combine(actionRoot, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(actionRoot);
        try
        {
            CopyActionSource(action.DirectoryPath, stagingDirectory);
            var entrypoint = Path.GetFullPath(Path.Combine(stagingDirectory, action.Manifest.Spec.Entrypoint));
            EnsureInside(stagingDirectory, entrypoint);
            if (!File.Exists(entrypoint))
                throw new InvalidOperationException($"Action '{action.Key}' entrypoint '{action.Manifest.Spec.Entrypoint}' does not exist.");

            if (action.Manifest.Spec.Runtime == AppActionRuntimes.Python)
                await PreparePythonAsync(stagingDirectory, cancellationToken);

            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, DeploymentMetadataFileName),
                JsonSerializer.Serialize(new { contentHash, preparedAtUtc = DateTime.UtcNow }),
                cancellationToken);

            if (Directory.Exists(currentDirectory))
            {
                var versionDirectory = Path.Combine(
                    workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, action.Key),
                    $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{HashSuffix(ReadCurrentHash(currentDirectory))}");
                Directory.CreateDirectory(Path.GetDirectoryName(versionDirectory)!);
                Directory.Move(currentDirectory, versionDirectory);
            }

            Directory.Move(stagingDirectory, currentDirectory);
            PruneVersions(appId, deploymentTargetId, action.Key);
            return new AppActionDeploymentResult
            {
                ActionKey = action.Key,
                Status = "deployed",
                ContentHash = contentHash,
                Message = $"Prepared {action.Manifest.Spec.Runtime} Action."
            };
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to prepare Action {ActionKey} for app {AppId}", action.Key, appId);
            throw new InvalidOperationException($"Failed to prepare Action '{action.Key}': {ex.Message}", ex);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static async Task PreparePythonAsync(string stagingDirectory, CancellationToken cancellationToken)
    {
        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        await RunProcessAsync(python, ["-m", "venv", ".venv"], stagingDirectory, DependencyPreparationTimeout, cancellationToken);

        var requirementsPath = Path.Combine(stagingDirectory, "requirements.txt");
        if (File.Exists(requirementsPath))
        {
            var venvPython = OperatingSystem.IsWindows()
                ? Path.Combine(stagingDirectory, ".venv", "Scripts", "python.exe")
                : Path.Combine(stagingDirectory, ".venv", "bin", "python");
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "-r", "requirements.txt"], stagingDirectory, DependencyPreparationTimeout, cancellationToken);
        }
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Runtime '{fileName}' could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Runtime preparation exceeded {timeout.TotalMinutes:0} minutes.");
        }

        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Runtime preparation failed with exit code {process.ExitCode}: {Limit(error.Length > 0 ? error : output, 2000)}");
    }

    private static void CopyActionSource(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is ".venv" or "__pycache__"))
                continue;
            var target = Path.Combine(destination, relativePath);
            EnsureInside(destination, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    internal static string ComputeContentHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Any(segment => segment is ".venv" or "__pycache__"))
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData([0]);
            using var stream = File.OpenRead(file);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    private static string? ReadCurrentHash(string currentDirectory)
    {
        var path = Path.Combine(currentDirectory, DeploymentMetadataFileName);
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("contentHash", out var value) ? value.GetString() : null;
    }

    private static AppActionVersionInfo ToVersionInfo(string path, string version, bool isCurrent)
    {
        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        var size = files.Sum(file => new FileInfo(file).Length);
        var created = Directory.GetCreationTimeUtc(path);
        return new(version, ReadCurrentHash(path), created, size, isCurrent);
    }

    private static void ValidatePreparedDeployment(string directory, string actionKey)
    {
        var manifestPath = Path.Combine(directory, "action.yaml");
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("Action version is missing action.yaml.");
        var manifest = AppActionManifestParser.ParseFile(manifestPath, actionKey, allowPrivilegedCapabilities: true);
        var entrypoint = Path.GetFullPath(Path.Combine(directory, manifest.Spec.Entrypoint));
        EnsureInside(directory, entrypoint);
        if (!File.Exists(entrypoint)) throw new InvalidOperationException("Action version entrypoint is missing.");
        if (manifest.Spec.Runtime == AppActionRuntimes.Python)
        {
            var python = OperatingSystem.IsWindows() ? Path.Combine(directory, ".venv", "Scripts", "python.exe") : Path.Combine(directory, ".venv", "bin", "python");
            if (!File.Exists(python)) throw new InvalidOperationException("Python Action version is missing its prepared virtual environment.");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var target = Path.Combine(destination, relative);
            EnsureInside(destination, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourceFile, target, overwrite: true);
        }
    }

    private static string HashSuffix(string? hash)
        => !string.IsNullOrWhiteSpace(hash) && hash.Length >= 20 ? hash[12..20] : "unknown";

    private static void ValidateVersionName(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version is "current" or "." or ".." || version.Length > 100
            || version.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException("Invalid Action version identifier.");
    }

    private void PruneVersions(Guid appId, Guid deploymentTargetId, string actionKey)
    {
        var root = workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey);
        if (!Directory.Exists(root)) return;
        var versions = Directory.GetDirectories(root)
            .Select(path => new { Path = path, Created = Directory.GetCreationTimeUtc(path), Size = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) })
            .OrderByDescending(item => item.Created).ToList();
        var retainedBytes = 0L;
        var maximumVersions = ReadPositiveIntEnvironment("AUTOMATON_ACTION_MAX_VERSIONS", DefaultMaximumVersionsPerAction, 1, 1000);
        var maximumBytes = ReadPositiveLongEnvironment("AUTOMATON_ACTION_MAX_VERSION_BYTES", DefaultMaximumVersionBytesPerAction, 1L * 1024 * 1024, 100L * 1024 * 1024 * 1024);
        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];
            if (index < maximumVersions && retainedBytes + version.Size <= maximumBytes)
            {
                retainedBytes += version.Size;
                continue;
            }
            Directory.Delete(version.Path, recursive: true);
        }
    }

    private static int ReadPositiveIntEnvironment(string name, int fallback, int minimum, int maximum)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static long ReadPositiveLongEnvironment(string name, long fallback, long minimum, long maximum)
        => long.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string EnsureChildPath(string root, string child)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(root, child));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Action version path must remain inside the versions directory.");
        return fullPath;
    }

    private static void EnsureInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Action files must stay inside the Action folder.");
    }

    private static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
