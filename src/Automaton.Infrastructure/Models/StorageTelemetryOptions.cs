namespace Automaton.Api.Models;

public sealed class StorageTelemetryOptions
{
    public decimal WarningPercent { get; set; } = 80m;
    public decimal DeploymentBlockPercent { get; set; } = 90m;
    public decimal OverLimitPercent { get; set; } = 100m;
    public int FreshAfterMinutes { get; set; } = 120;
    public int CriticalStaleAfterMinutes { get; set; } = 240;
    public int AuthoritativeValidityMinutes { get; set; } = 240;
    public int ObservationRetentionDays { get; set; } = 30;
    public long MinimumHostReserveBytes { get; set; } = 15L * 1024 * 1024 * 1024;
    public decimal HostReservePercent { get; set; } = 15m;
    public int InitialTelemetryGraceMinutes { get; set; } = 30;
    public int ReconciliationIntervalMinutes { get; set; } = 20;
    public int VerificationTimeoutMinutes { get; set; } = 10;
    public int VerificationWindowMinutes { get; set; } = 60;
    public int MaxVerificationDispatchesPerHost { get; set; } = 2;
    /// <summary>Optional sampled audit mode; normal reconciliation does not poll healthy Runners.</summary>
    public bool EnableLowFrequencyVerification { get; set; }
    public int LowFrequencyVerificationHours { get; set; } = 24;
    public decimal LowFrequencyVerificationSamplePercent { get; set; } = 1m;
}
