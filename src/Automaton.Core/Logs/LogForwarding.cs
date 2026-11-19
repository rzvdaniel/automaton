using Serilog.Core;
using Serilog.Events;

namespace Automaton.Core.Logs;

/// <summary>
/// The process's switchable log shipping. Installed when no <c>Logging:Otlp:Endpoint</c> is configured; the process then
/// calls <see cref="SetAddress"/> with the address of an installed app that receives logs, or null when there is none.
/// </summary>
public static class LogForwarding
{
    private static LogForwardingSink? current;

    /// <summary>True when this process can be switched; false when the admin set an endpoint or logging is not built yet.</summary>
    public static bool IsSwitchable => Volatile.Read(ref current) is not null;

    internal static void Install(LogForwardingSink? sink) => Volatile.Write(ref current, sink);

    /// <summary>Ships logs to <paramref name="address"/> (a bare http(s) address gets the OTLP logs path), or stops for null or an invalid value.</summary>
    public static void SetAddress(string? address) => Volatile.Read(ref current)?.SetEndpoint(OtlpLogShipping.ResolveEndpoint(address));
}

/// <summary>A Serilog sink that forwards to an OTLP sink for the current endpoint, and drops events while there is none.</summary>
internal sealed class LogForwardingSink(Func<string, ILogEventSink> create) : ILogEventSink, IDisposable
{
    private readonly object gate = new();
    private string? endpoint;
    private ILogEventSink? inner;

    public void SetEndpoint(string? value) => SetEndpoint(value, disposeInBackground: true);

    private void SetEndpoint(string? value, bool disposeInBackground)
    {
        ILogEventSink? old;
        lock (gate)
        {
            if (value == endpoint) return;
            old = inner;
            inner = value is null ? null : create(value);
            endpoint = value;
        }
        if (old is not IDisposable disposable) return;
        // Disposing flushes what is queued for the old address. When that address no longer answers (its app was
        // restarted or removed) the flush waits for a timeout of over a minute; switching must not wait for it, or the
        // caller (the 30 s poll, the Runner's heartbeat) is stuck and never reaches the new address.
        if (disposeInBackground) _ = Task.Run(() => DisposeQuietly(disposable));
        else DisposeQuietly(disposable);
    }

    private static void DisposeQuietly(IDisposable sink)
    {
        try { sink.Dispose(); }
        catch (Exception) { /* the old address is gone; nothing to report to it */ }
    }

    public void Emit(LogEvent logEvent) => Volatile.Read(ref inner)?.Emit(logEvent);

    public void Dispose() => SetEndpoint(null, disposeInBackground: false);
}
