namespace Automaton.Cli.Models;

internal sealed record AppInventoryItem(
    Guid Id, string Name, bool CanLaunchPublicly, string? PublicUrl, AppInventoryRunner? Runner, AppInventoryTarget? LiveEnvironment, bool IsPaused = false);

internal sealed record AppInventoryRunner(string Name);

internal sealed record AppInventoryTarget(string Status);
