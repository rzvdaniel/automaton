using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Automaton.Api.Services;

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Automaton.Secrets", "SecretVersion", "v1");
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("A secret value is required.", nameof(plaintext));
        return protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            throw new ArgumentException("A protected secret value is required.", nameof(protectedValue));
        return protector.Unprotect(protectedValue);
    }
}
