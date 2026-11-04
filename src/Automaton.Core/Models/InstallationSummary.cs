namespace Automaton.Core.Models;

public sealed record InstallationSummary(string? Version, int Users, int Apps, int Runners, DateTimeOffset? LastBackupAt);
