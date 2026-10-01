namespace Automaton.Api.Services;

/// <summary>A persisted terminal synchronization outcome; retrying its operation cannot recover it.</summary>
public sealed class ManagedApplicationArtifactFailureException(string message) : InvalidOperationException(message);
