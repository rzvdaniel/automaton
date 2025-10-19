using Polly;

namespace Automaton.Runner.Console.Connection;

public interface IConnectionPolicy
{
    IAsyncPolicy GetPolicy();
    bool ShouldStopRetrying();
}