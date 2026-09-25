namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeContainerArtifactStore(RunnerWorkspaceService workspace)
{
    public IEnumerable<string> EnumerateDeploymentMetadataPaths()
        => Directory.Exists(workspace.AppsRoot)
            ? Directory.EnumerateFiles(workspace.AppsRoot, "deployment.json", SearchOption.AllDirectories)
            : [];

    public bool Exists(string? path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public void CreateRoot(string path)
        => Directory.CreateDirectory(path);

    public string GetArtifactId(string artifactRoot)
        => Path.GetFileName(Path.GetDirectoryName(artifactRoot) ?? artifactRoot);

    public string ValidateRoot(Guid appId, Guid targetId, string path)
    {
        var candidateRoot = Path.GetFullPath(workspace.GetAppRuntimeCandidateDirectory(appId, targetId, Guid.NewGuid()));
        var artifact = Path.GetFullPath(path);
        var candidatesRoot = Path.GetDirectoryName(candidateRoot)!;
        if (!artifact.StartsWith(candidatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Application artifact is outside the managed candidate boundary.");
        return artifact;
    }

    public static void TryDeletePrevious(string artifactRoot, ICollection<string> warnings)
    {
        if (!Directory.Exists(artifactRoot)) return;
        try { Directory.Delete(artifactRoot, recursive: true); }
        catch (Exception ex) { warnings.Add($"Previous runtime artifact cleanup deferred: {ex.Message}"); }
    }
}
