using System.Diagnostics;
using System.Text;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Runs a script the server sent as root on this machine (<c>host-exec</c>), through a short-lived privileged container in the host's
/// namespaces like the other host commands. The script is an argument; stdin carries secrets, so they never appear in arguments or the
/// environment (<c>ps</c>), nothing is written to disk, and output that repeats an input line is redacted before it leaves the Runner.
/// </summary>
internal sealed class HostExecService(HostExecService.ProcessRunner? process = null)
{
    internal delegate Task<(int ExitCode, string Output, string Error, bool TimedOut)> ProcessRunner(
        string fileName, IReadOnlyList<string> arguments, string stdin, TimeSpan timeout, Func<Task> onTimeout, CancellationToken token);

    internal const int MaximumOutputLength = 200_000;
    internal const string Redacted = "***";
    private const string HostPath = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    private readonly ProcessRunner run = process ?? RunProcessAsync;

    public async Task<HostExecResult> RunAsync(HostExecRequest request, CancellationToken token)
    {
        Validate(request);
        var stdin = request.Stdin ?? "";
        var timeout = TimeSpan.FromSeconds(request.TimeoutSeconds);
        var started = DateTime.UtcNow;
        var image = await OwnImageAsync(token);
        var name = "automaton-host-exec-" + Guid.NewGuid().ToString("N");
        var result = await run("docker", Arguments(image, name, request.Script), stdin, timeout,
            () => run("docker", ["kill", name], "", TimeSpan.FromSeconds(30), () => Task.CompletedTask, CancellationToken.None), token);
        var secrets = Secrets(stdin);
        return new(result.TimedOut ? -1 : result.ExitCode, result.TimedOut,
            Redact(Tail(result.Output), secrets), Redact(Tail(result.Error), secrets), started, DateTime.UtcNow);
    }

    internal static void Validate(HostExecRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Script)) throw new InvalidDataException("The script is empty.");
        if (request.Script.Length > HostExecRequest.MaximumScriptLength || request.Script.Contains('\0'))
            throw new InvalidDataException("The script is too long or contains a null character.");
        if ((request.Stdin?.Length ?? 0) > HostExecRequest.MaximumStdinLength) throw new InvalidDataException("The input is too long.");
        if (request.TimeoutSeconds is < 1 or > HostExecRequest.MaximumTimeoutSeconds)
            throw new InvalidDataException($"The timeout must be from 1 to {HostExecRequest.MaximumTimeoutSeconds} seconds.");
    }

    internal static IReadOnlyList<string> Arguments(string image, string containerName, string script)
        => ["run", "--rm", "-i", "--name", containerName, "--privileged", "--pid=host", "--user", "root", "--entrypoint", "nsenter", image,
            "-t", "1", "-m", "-u", "-i", "-n", "-p", "--", "/usr/bin/env", "PATH=" + HostPath, "bash", "--noprofile", "--norc", "-c", script];

    /// <summary>The whole input and each of its lines (three characters or more), longest first so a long secret is replaced before its parts.</summary>
    internal static IReadOnlyList<string> Secrets(string stdin)
        => stdin.Split('\n').Append(stdin).Select(item => item.Trim()).Where(item => item.Length >= 3).Distinct()
            .OrderByDescending(item => item.Length).Take(200).ToArray();

    internal static string Redact(string text, IReadOnlyList<string> secrets)
        => secrets.Aggregate(text, (current, secret) => current.Replace(secret, Redacted, StringComparison.Ordinal));

    private static string Tail(string text) => text.Length <= MaximumOutputLength ? text : "[earlier output omitted]\n" + text[^MaximumOutputLength..];

    private async Task<string> OwnImageAsync(CancellationToken token)
    {
        var own = await run("docker", ["inspect", "--format", "{{.Image}}", Environment.MachineName], "", TimeSpan.FromSeconds(30), () => Task.CompletedTask, token);
        if (own.ExitCode != 0) throw new InvalidOperationException("This Runner cannot inspect its own container, so it cannot reach the host.");
        return own.Output.Trim();
    }

    internal static async Task<(int ExitCode, string Output, string Error, bool TimedOut)> RunProcessAsync(
        string fileName, IReadOnlyList<string> arguments, string stdin, TimeSpan timeout, Func<Task> onTimeout, CancellationToken token)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + fileName + ".");
        var output = ReadCappedAsync(child.StandardOutput);
        var error = ReadCappedAsync(child.StandardError);
        try
        {
            await child.StandardInput.WriteAsync(stdin);
            child.StandardInput.Close();
        }
        catch (IOException) { /* the script ended without reading its input */ }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            await child.WaitForExitAsync(limit.Token);
            return (child.ExitCode, await output, await error, false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await onTimeout();
            try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await child.WaitForExitAsync(CancellationToken.None);
            return (-1, await output, await error, true);
        }
        catch (OperationCanceledException)
        {
            await onTimeout();
            try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    /// <summary>Reads to the end but keeps only the last <see cref="MaximumOutputLength"/> characters, so a noisy script cannot fill memory.</summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var kept = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            kept.Append(buffer, 0, count);
            if (kept.Length > MaximumOutputLength * 2) kept.Remove(0, kept.Length - MaximumOutputLength);
        }
        return kept.ToString();
    }
}
