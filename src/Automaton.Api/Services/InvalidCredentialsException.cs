namespace Automaton.Api.Services;

/// <summary>Sign-in failed: unknown email, deleted user, locked out or wrong password. One message for all, so it does not reveal which accounts exist.</summary>
public sealed class InvalidCredentialsException() : Exception(DefaultMessage)
{
    public const string DefaultMessage = "Wrong email or password.";
}
