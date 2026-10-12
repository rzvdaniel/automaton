using Automaton.Node.Capabilities;

namespace Automaton.Node.Preparation;

public enum NodePreparationPhase { Created, Running, Stopped }

/// <summary>Process-local liveness only; never enrollment, ownership or admission authority.</summary>
public sealed class NodePreparationState
{
    private int phase;

    public NodePreparationPhase Phase => (NodePreparationPhase)Volatile.Read(ref phase);
    public NodeCapabilityValidation Capabilities { get; } = NodeCapabilityValidator.Validate(null);

    internal bool TryStart() => Interlocked.CompareExchange(
        ref phase, (int)NodePreparationPhase.Running, (int)NodePreparationPhase.Created)
        == (int)NodePreparationPhase.Created;

    internal bool TryStop() => Interlocked.Exchange(ref phase, (int)NodePreparationPhase.Stopped)
        != (int)NodePreparationPhase.Stopped;
}
