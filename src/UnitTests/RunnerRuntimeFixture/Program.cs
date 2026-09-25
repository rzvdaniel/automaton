using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace RunnerRuntimeFixture;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var exitCode = await FixtureCommandHandler.TryRunAsync(args);
        if (exitCode.HasValue)
            return exitCode.Value;

        await FixtureServer.RunAsync(args);
        return 0;
    }
}

internal static class FixtureCommandHandler
{
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (TryRunOrphanChild(args))
            return 0;
        if (TryWriteFile(args))
            return 0;
        if (TryRequireFile(args, out var requiredFileExitCode))
            return requiredFileExitCode;
        if (TryFailWithOutput(args))
            return 3;
        if (await TryDelayAsync(args))
            return 0;

        return null;
    }

    private static bool TryRunOrphanChild(string[] args)
    {
        var index = Array.IndexOf(args, "--orphan-child");
        if (index < 0 || index + 1 >= args.Length)
            return false;

        var orphan = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = false,
            Environment = { ["AUTOMATON_PORT"] = "1" },
            ArgumentList =
            {
                Assembly.GetExecutingAssembly().Location,
                "--child-process"
            }
        }) ?? throw new InvalidOperationException("Could not start orphan child.");
        File.WriteAllText(args[index + 1], orphan.Id.ToString());
        Console.WriteLine($"orphan-child:{orphan.Id}");
        Console.Out.Flush();
        return true;
    }

    private static bool TryWriteFile(string[] args)
    {
        var index = Array.IndexOf(args, "--write-file");
        if (index < 0 || index + 2 >= args.Length)
            return false;

        File.WriteAllText(args[index + 1], args[index + 2]);
        return true;
    }

    private static bool TryRequireFile(string[] args, out int exitCode)
    {
        exitCode = 0;
        var index = Array.IndexOf(args, "--require-file");
        if (index < 0 || index + 1 >= args.Length || File.Exists(args[index + 1]))
            return false;

        Console.Error.WriteLine($"required file missing: {args[index + 1]}");
        exitCode = 2;
        return true;
    }

    private static bool TryFailWithOutput(string[] args)
    {
        if (!args.Contains("--fail-with-output", StringComparer.Ordinal))
            return false;

        Console.WriteLine("useful stdout diagnostic");
        return true;
    }

    private static async Task<bool> TryDelayAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--delay");
        if (index < 0 || index + 1 >= args.Length)
            return false;

        await Task.Delay(int.Parse(args[index + 1]));
        return true;
    }
}

internal static class FixtureServer
{
    public static async Task RunAsync(string[] args)
    {
        var options = FixtureServerOptions.Parse(args);
        WriteStartupOutput(args);
        await WaitForStartupDelayAsync(args);

        if (options.ChildMode)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }

        using var child = StartChildIfRequested(args);
        await RunTcpServerAsync(options, child);
    }

    private static void WriteStartupOutput(string[] args)
    {
        Console.WriteLine($"stdout:{string.Join('|', args)}:{Environment.GetEnvironmentVariable("TEST_RUNTIME_VALUE")}:" +
            $"{Environment.GetEnvironmentVariable("AUTOMATON_APP_DATA_DIR")}:" +
            $"{Environment.GetEnvironmentVariable("AUTOMATON_STORAGE_UPLOADS")}");
        Console.Error.WriteLine("stderr:fixture");
        Console.Out.Flush();
        Console.Error.Flush();
    }

    private static async Task WaitForStartupDelayAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--startup-delay");
        if (index >= 0 && index + 1 < args.Length)
            await Task.Delay(int.Parse(args[index + 1]));
    }

    private static Process? StartChildIfRequested(string[] args)
    {
        if (!args.Contains("--spawn-child", StringComparer.Ordinal))
            return null;

        var child = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = false,
            ArgumentList =
            {
                Assembly.GetExecutingAssembly().Location,
                "--child-process"
            }
        });
        Console.WriteLine($"child:{child?.Id}");
        Console.Out.Flush();
        return child;
    }

    private static async Task RunTcpServerAsync(FixtureServerOptions options, Process? child)
    {
        var listener = new TcpListener(IPAddress.Loopback, options.Port);
        var requests = 0;
        listener.Start();
        try
        {
            while (listener.Server.IsBound)
            {
                using var client = await listener.AcceptTcpClientAsync();
                if (options.Http)
                    await RespondToHttpRequestAsync(client, options, requests++);
            }
        }
        finally
        {
            listener.Stop();
            if (child is not null && !child.HasExited)
                child.Kill(entireProcessTree: true);
        }
    }

    private static async Task RespondToHttpRequestAsync(TcpClient client, FixtureServerOptions options, int requestIndex)
    {
        using var stream = client.GetStream();
        var buffer = new byte[1024];
        _ = await stream.ReadAsync(buffer);
        var unavailable = options.AlwaysUnavailable || (options.UnavailableOnce && requestIndex == 0);
        var body = Encoding.UTF8.GetBytes(unavailable ? "unavailable" : "ready");
        var status = unavailable ? "503 Service Unavailable" : "200 OK";
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.WriteAsync(body);
    }
}

internal sealed record FixtureServerOptions(
    int Port,
    bool Http,
    bool AlwaysUnavailable,
    bool UnavailableOnce,
    bool ChildMode)
{
    public static FixtureServerOptions Parse(string[] args)
    {
        var alwaysUnavailable = args.Contains("--http-503", StringComparer.Ordinal);
        var unavailableOnce = args.Contains("--http-503-once", StringComparer.Ordinal);
        var http = args.Contains("--http", StringComparer.Ordinal) || alwaysUnavailable || unavailableOnce;
        var port = int.Parse(Environment.GetEnvironmentVariable("AUTOMATON_PORT")!);
        return new FixtureServerOptions(
            port,
            http,
            alwaysUnavailable,
            unavailableOnce,
            args.Contains("--child-process", StringComparer.Ordinal));
    }
}
