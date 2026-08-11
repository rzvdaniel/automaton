using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Automaton.AgentOrchestrator.Domain;

namespace Automaton.AgentOrchestrator.Codex;

public sealed record CodexProcessOptions(
    string Command,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? ShutdownTimeout = null,
    string? LaunchNonce = null);

public sealed class ProcessCodexJsonRpcTransport(CodexProcessOptions options) : ICodexJsonRpcTransport
{
    private static readonly string[] AllowedHostEnvironment = ["PATH", "Path", "PATHEXT", "SYSTEMROOT", "SystemRoot", "COMSPEC", "TEMP", "TMP", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "CODEX_HOME"];
    private readonly Channel<string> stdout = Channel.CreateUnbounded<string>(new() { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private Task? stdoutReader;
    private Task? stderrReader;
    private string stderrTail = string.Empty;

    public string LaunchNonce { get; } = options.LaunchNonce ?? Guid.NewGuid().ToString("N");
    public CodexProcessIdentity? Identity { get; private set; }
    internal IReadOnlyDictionary<string, string>? StartedEnvironment { get; private set; }
    internal string StderrTail => stderrTail;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (process is not null) throw new InvalidOperationException("Codex App Server process is already started.");
        var (fileName, arguments) = ParseCommand(options.Command);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment.Clear();
        var hostEnvironment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => Convert.ToString(entry.Value) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in BuildEnvironment(hostEnvironment, options.Environment)) startInfo.Environment[pair.Key] = pair.Value;
        StartedEnvironment = new Dictionary<string, string>(startInfo.Environment.ToDictionary(x => x.Key, x => x.Value ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        process = Process.Start(startInfo) ?? throw new CodexProtocolException("Unable to launch Codex App Server.");
        Identity = new(process.Id, process.StartTime.ToUniversalTime(), LaunchNonce);
        stdoutReader = ReadStdoutAsync(process, lifetime.Token);
        stderrReader = ReadStderrAsync(process, lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        var current = process ?? throw new InvalidOperationException("Codex App Server process has not started.");
        if (current.HasExited) throw new CodexProcessExitedException();
        await current.StandardInput.WriteLineAsync(message.AsMemory(), cancellationToken);
        await current.StandardInput.FlushAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in stdout.Reader.ReadAllAsync(cancellationToken)) yield return line;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (process is null) return;
        if (!process.HasExited)
        {
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.ShutdownTimeout ?? TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        lifetime.Cancel();
        if (stdoutReader is not null) await IgnoreCancellation(stdoutReader);
        if (stderrReader is not null) await IgnoreCancellation(stderrReader);
    }

    private async Task ReadStdoutAsync(Process current, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await current.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;
                await stdout.Writer.WriteAsync(line, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { stdout.Writer.TryComplete(); }
    }

    private async Task ReadStderrAsync(Process current, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await current.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
                stderrTail = (stderrTail + Environment.NewLine + line);
                if (stderrTail.Length > 4096) stderrTail = stderrTail[^4096..];
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal static (string FileName, IReadOnlyList<string> Arguments) ParseCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new CodexProtocolException("codex.command must not be empty.");
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in command)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted) { if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); } continue; }
            current.Append(character);
        }
        if (quoted) throw new CodexProtocolException("codex.command contains an unterminated quote.");
        if (current.Length > 0) tokens.Add(current.ToString());
        if (tokens.Count == 0) throw new CodexProtocolException("codex.command must not be empty.");
        return (tokens[0], tokens.Skip(1).ToArray());
    }

    internal static IReadOnlyDictionary<string, string> BuildEnvironment(
        IReadOnlyDictionary<string, string> hostEnvironment,
        IReadOnlyDictionary<string, string>? explicitEnvironment)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in AllowedHostEnvironment.Distinct(StringComparer.OrdinalIgnoreCase))
            if (hostEnvironment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)) result[name] = value;
        foreach (var pair in explicitEnvironment ?? new Dictionary<string, string>())
        {
            if (IsForbiddenChildEnvironmentName(pair.Key))
                throw new CodexProtocolException($"Child environment variable '{pair.Key}' is not allowed.");
            result[pair.Key] = pair.Value;
        }
        return result;
    }

    private static bool IsForbiddenChildEnvironmentName(string name)
        => name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
           || name.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
           || name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
           || name.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase)
           || name.Contains("DATABASE", StringComparison.OrdinalIgnoreCase)
           || name.Contains("RABBIT", StringComparison.OrdinalIgnoreCase)
           || name.Contains("RUNNER", StringComparison.OrdinalIgnoreCase)
           || name.Contains("GITHUB", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("GH_", StringComparison.OrdinalIgnoreCase);

    private static async Task IgnoreCancellation(Task task) { try { await task; } catch (OperationCanceledException) { } }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        process?.Dispose();
        lifetime.Dispose();
    }
}
