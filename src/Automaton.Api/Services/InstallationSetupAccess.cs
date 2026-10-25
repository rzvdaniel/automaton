using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

public static class InstallationSetupAccess
{
    public const string HeaderName = "X-Automaton-Setup-Token";

    public static bool IsAllowed(IConfiguration configuration, IHostEnvironment environment, string? suppliedToken)
    {
        var expected = configuration["Bootstrap:Token"];
        if (string.IsNullOrWhiteSpace(expected)) return environment.IsDevelopment();
        if (string.IsNullOrWhiteSpace(suppliedToken) || suppliedToken.Length > 512) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken)));
    }
}
