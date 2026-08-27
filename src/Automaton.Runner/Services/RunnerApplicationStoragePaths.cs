namespace Automaton.Runner.Console.Services;

/// <summary>
/// Infrastructure-owned roots for one application deployment target. The
/// application may choose any layout inside SourceRoot, while the runner keeps
/// generated artifacts, process state, logs, cache, and persistent data apart.
/// </summary>
public sealed record RunnerApplicationStoragePaths(
    string ApplicationRoot,
    string WorkspaceRoot,
    string SourceRoot,
    string AutomationRoot,
    string ArtifactsRoot,
    string RuntimeRoot,
    string LogsRoot,
    string CacheRoot,
    string PersistentDataRoot);

public sealed record RunnerApplicationStorageRuntime(
    string DataDirectory,
    IReadOnlyDictionary<string, string> Mounts);
