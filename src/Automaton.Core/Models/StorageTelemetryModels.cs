namespace Automaton.Core.Models;

public sealed record MachineRunnerStorageTelemetry(
    DateTime MeasuredAtUtc,
    long TotalBytes,
    long AvailableBytes,
    bool Success,
    string? Error,
    string? RunnerVersion);
