namespace Automaton.Runner.Console.Services;

public enum InfrastructureCommandConsumerReadiness
{
    NotReady,
    Starting,
    Ready
}

/// <summary>Exposes command-consumer readiness independently from the Runner heartbeat.</summary>
public sealed class InfrastructureCommandConsumerStatus
{
    private readonly object gate = new();
    private InfrastructureCommandConsumerReadiness readiness = InfrastructureCommandConsumerReadiness.NotReady;
    private string? detail;

    public InfrastructureCommandConsumerReadiness Readiness
    {
        get
        {
            lock (gate)
                return readiness;
        }
    }

    public string? Detail
    {
        get
        {
            lock (gate)
                return detail;
        }
    }

    internal void Set(InfrastructureCommandConsumerReadiness next, string? nextDetail = null)
    {
        lock (gate)
        {
            readiness = next;
            detail = nextDetail;
        }
    }
}
