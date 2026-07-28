namespace Automaton.Api.Services;

public sealed class AppTagValidationException : InvalidOperationException
{
    public AppTagValidationException(string message)
        : base(message)
    {
    }
}
