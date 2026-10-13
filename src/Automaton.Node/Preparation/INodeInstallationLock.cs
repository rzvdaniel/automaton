namespace Automaton.Node.Preparation;

/// <summary>Host-wide interprocess ownership boundary for the local Node manager.</summary>
internal interface INodeInstallationLock
{
    void Acquire(CancellationToken cancellationToken);
    void Release();
}
