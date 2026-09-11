namespace Automaton.Runner.Console.Services;

internal enum ManagedHostedRunnerCredentialDecision
{
    ReusePersistedCredential,
    Enroll,
    FailClosed
}
