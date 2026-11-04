namespace Automaton.Cli.Models;

internal sealed record AppInventoryItem(
    Guid Id, string Name, bool CanLaunchPublicly, string? PublicUrl, AppInventoryRunner? Runner, AppInventoryTarget? LiveEnvironment);

internal sealed record AppInventoryRunner(string Name);

internal sealed record AppInventoryTarget(string Status);
