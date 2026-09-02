using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Text;

var alwaysUnavailable = args.Contains("--http-503", StringComparer.Ordinal);
var unavailableOnce = args.Contains("--http-503-once", StringComparer.Ordinal);
var http = args.Contains("--http", StringComparer.Ordinal) || alwaysUnavailable || unavailableOnce;
var childMode = args.Contains("--child-process", StringComparer.Ordinal);
var orphanChildIndex = Array.IndexOf(args, "--orphan-child");
if (orphanChildIndex >= 0 && orphanChildIndex + 1 < args.Length)
{
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
    File.WriteAllText(args[orphanChildIndex + 1], orphan.Id.ToString());
    Console.WriteLine($"orphan-child:{orphan.Id}");
    Console.Out.Flush();
    return;
}

var writeFileIndex = Array.IndexOf(args, "--write-file");
if (writeFileIndex >= 0 && writeFileIndex + 2 < args.Length)
{
    File.WriteAllText(args[writeFileIndex + 1], args[writeFileIndex + 2]);
    return;
}

var requiredFileIndex = Array.IndexOf(args, "--require-file");
if (requiredFileIndex >= 0 && requiredFileIndex + 1 < args.Length
    && !File.Exists(args[requiredFileIndex + 1]))
{
    Console.Error.WriteLine($"required file missing: {args[requiredFileIndex + 1]}");
    Environment.ExitCode = 2;
    return;
}

if (args.Contains("--fail-with-output", StringComparer.Ordinal))
{
    Console.WriteLine("useful stdout diagnostic");
    Environment.ExitCode = 3;
    return;
}

var delayIndex = Array.IndexOf(args, "--delay");
if (delayIndex >= 0 && delayIndex + 1 < args.Length)
{
    await Task.Delay(int.Parse(args[delayIndex + 1]));
    return;
}

var port = int.Parse(Environment.GetEnvironmentVariable("AUTOMATON_PORT")!);
Console.WriteLine($"stdout:{string.Join('|', args)}:{Environment.GetEnvironmentVariable("TEST_RUNTIME_VALUE")}:" +
    $"{Environment.GetEnvironmentVariable("AUTOMATON_APP_DATA_DIR")}:" +
    $"{Environment.GetEnvironmentVariable("AUTOMATON_STORAGE_UPLOADS")}");
Console.Error.WriteLine("stderr:fixture");
Console.Out.Flush();
Console.Error.Flush();

if (childMode)
{
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

Process? child = null;
if (args.Contains("--spawn-child", StringComparer.Ordinal))
{
    child = Process.Start(new ProcessStartInfo
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
}

var listener = new TcpListener(IPAddress.Loopback, port);
var requests = 0;
listener.Start();
try
{
    while (true)
    {
        using var client = await listener.AcceptTcpClientAsync();
        if (!http)
            continue;

        using var stream = client.GetStream();
        var buffer = new byte[1024];
        _ = await stream.ReadAsync(buffer);
        var unavailable = alwaysUnavailable || (unavailableOnce && requests++ == 0);
        var body = Encoding.UTF8.GetBytes(unavailable ? "unavailable" : "ready");
        var status = unavailable ? "503 Service Unavailable" : "200 OK";
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.WriteAsync(body);
    }
}
finally
{
    listener.Stop();
    if (child is not null && !child.HasExited)
        child.Kill(entireProcessTree: true);
}
