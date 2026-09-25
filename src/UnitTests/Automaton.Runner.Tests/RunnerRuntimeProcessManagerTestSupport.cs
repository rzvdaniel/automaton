using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerTestSupport
{
    internal static RunnerRuntimeProcessManager CreateManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        Func<RunnerRuntimeDeploymentMetadata, Exception?>? metadataWriteFailureFactory = null)
        => new(
            workspace,
            registry,
            NullLogger<RunnerRuntimeProcessManager>.Instance,
            metadataWriteFailureFactory);

    internal static void ConfigureRuntimePorts(int? gatewayPort, int portStart, int portEnd)
    {
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", gatewayPort?.ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START", portStart.ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END", portEnd.ToString());
    }

    internal static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    internal static int GetFreePort(params int[] excluding)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (!excluding.Contains(port))
                return port;
        }

        throw new InvalidOperationException("Could not find a free port for the runtime test.");
    }

    internal static (int GatewayPort, int FallbackPort) GetAdjacentFreePorts()
    {
        for (var port = 20_000; port < 65_534; port++)
        {
            if (CanBind(port) && CanBind(port + 1))
                return (port, port + 1);
        }

        throw new InvalidOperationException("Could not find adjacent free ports for the runtime test.");
    }

    internal static string CreateRuntimePackage(string root, 
        Guid appId,
        IReadOnlyList<string>? arguments = null,
        string marker = "deployed",
        ApplicationBuildSpec? build = null)
    {
        var packagePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(root);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var contract = new ApplicationContract
        {
            Build = build ?? new ApplicationBuildSpec(),
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = arguments?.ToList() ?? ["RunnerRuntimeFixture.dll", "--tcp", "deployed"],
                    WorkingDirectory = "."
                },
                Port = 20_000 + Random.Shared.Next(1000)
            },
            Health = new ApplicationHealthSpec { Path = null, StartupTimeoutSeconds = 5 }
        };
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AppDeploymentPackageManifest
        {
            AppId = appId.ToString(),
            Application = contract
        }));

        var fixtureDirectory = Path.GetDirectoryName(FixturePath())!;
        foreach (var file in Directory.GetFiles(fixtureDirectory, "RunnerRuntimeFixture.*"))
            archive.CreateEntryFromFile(file, $"runtime/application/{Path.GetFileName(file)}");
        WriteEntry(archive, "runtime/application/marker.txt", marker);
        if (build?.Command is not null)
        {
            WriteEntry(archive, "source/build-input.txt", "generic build input");
            foreach (var file in Directory.GetFiles(fixtureDirectory, "RunnerRuntimeFixture.*"))
                archive.CreateEntryFromFile(file, $"source/{Path.GetFileName(file)}");
        }
        return packagePath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    internal static RunnerRuntimeDeploymentMetadata ReadMetadata(string path)
        => JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    internal static async Task<RunnerRuntimeDeploymentMetadata> WaitForCandidateAsync(string metadataPath)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var metadata = ReadMetadata(metadataPath);
                if (metadata.Candidate is not null)
                    return metadata;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }

            await Task.Delay(50);
        }

        Assert.Fail("Replacement candidate did not enter readiness before the timeout.");
        return null!;
    }

    internal static async Task<bool> WaitForOldRuntimeExitCallbackAsync(
        RunnerRuntimeRegistry registry,
        Guid appId,
        Guid targetId,
        string metadataPath,
        Task replacementTask)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                var metadata = ReadMetadata(metadataPath);
                if (!replacementTask.IsCompleted
                    && metadata.Candidate is not null
                    && !registry.TryGet(appId, targetId, out _))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                await Task.Delay(50);
                continue;
            }

            await Task.Delay(50);
        }

        return false;
    }

    internal static Process StartFixtureProcess(string workingDirectory, int port, string value)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { ["AUTOMATON_PORT"] = port.ToString(), ["TEST_RUNTIME_VALUE"] = value },
            ArgumentList = { FixturePath(), "--tcp", value }
        }) ?? throw new InvalidOperationException("Could not start runtime fixture.");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    internal static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!IsProcessAlive(processId))
                return true;
            await Task.Delay(100);
        }
        return !IsProcessAlive(processId);
    }

    internal static async Task<string> WaitForLogLineAsync(string path, string marker)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(path))
            {
                var content = await ReadSharedTextAsync(path);
                if (content.Contains(marker, StringComparison.Ordinal))
                    return content;
            }
            await Task.Delay(100);
        }
        return await ReadSharedTextAsync(path);
    }

    internal static ApplicationContract CreateContract(IReadOnlyList<string> arguments)
        => new()
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [FixturePath(), .. arguments],
                    WorkingDirectory = ".",
                    Environment = new Dictionary<string, string> { ["TEST_RUNTIME_VALUE"] = "expected" }
                },
                Port = 20_000 + Random.Shared.Next(1000)
            },
            Health = new ApplicationHealthSpec { Path = null, StartupTimeoutSeconds = 5 }
        };

    internal static string FixturePath()
        => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "RunnerRuntimeFixture", "bin", "Debug", "net9.0", "RunnerRuntimeFixture.dll"));

    internal static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
