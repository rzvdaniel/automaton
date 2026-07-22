namespace Automaton.Api.Services.Interfaces;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
