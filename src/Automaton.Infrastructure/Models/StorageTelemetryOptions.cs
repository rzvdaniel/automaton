namespace Automaton.Api.Models;

public sealed class StorageTelemetryOptions
{
    public int FreshAfterMinutes { get; set; } = 120;
    public long MinimumHostReserveBytes { get; set; } = 5L * 1024 * 1024 * 1024;
    public decimal HostReservePercent { get; set; } = 15m;
}
