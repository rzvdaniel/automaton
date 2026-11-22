using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

/// <summary>
/// The server setup token. The installer writes it to a root-only file and prints it so the first administrator can be
/// created. Where that file is mounted (<c>Bootstrap:TokenFile</c>) the file is the only source and it is replaced by a
/// fresh random value once setup completes, so a token seen in installer output or a transcript stops working while
/// root can still read the file to create one-time password links. Without a file (development, older installations)
/// the configured <c>Bootstrap:Token</c> is used unchanged.
/// </summary>
public static class InstallationSetupAccess
{
    public const string HeaderName = "X-Automaton-Setup-Token";

    public static string? ExpectedToken(IConfiguration configuration)
    {
        var path = configuration["Bootstrap:TokenFile"];
        if (string.IsNullOrWhiteSpace(path)) return configuration["Bootstrap:Token"];
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool IsAllowed(IConfiguration configuration, IHostEnvironment environment, string? suppliedToken)
    {
        var expected = ExpectedToken(configuration);
        if (string.IsNullOrWhiteSpace(expected)) return environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuration["Bootstrap:TokenFile"]);
        if (string.IsNullOrWhiteSpace(suppliedToken) || suppliedToken.Length > 512) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken)));
    }

    /// <summary>
    /// Replaces the token file's content in place (the file is a bind mount, so it is rewritten, not replaced) with a new
    /// random value that is never printed or returned. Returns false when there is no token file or it cannot be written.
    /// </summary>
    public static bool Rotate(IConfiguration configuration)
    {
        var path = configuration["Bootstrap:TokenFile"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            stream.SetLength(0);
            stream.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + "\n"));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
