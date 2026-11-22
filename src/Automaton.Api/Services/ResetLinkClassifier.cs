using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Services;

public static class ResetLinkClassifier
{
    /// <summary>
    /// Identity reports a used and an expired link the same way. The token starts with its creation time, so an
    /// old one is "expired" and a recent one that no longer works was "used" (the reset changes the security stamp).
    /// Returns "invalid" when the code is not a token from this server.
    /// </summary>
    public static string Classify(IDataProtectionProvider provider, DataProtectionTokenProviderOptions options, string token, DateTimeOffset now)
    {
        try
        {
            var payload = provider.CreateProtector("DataProtectorTokenProvider").Unprotect(Convert.FromBase64String(token));
            using var reader = new BinaryReader(new MemoryStream(payload));
            var created = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            return created + options.TokenLifespan < now ? "expired" : "used";
        }
        catch (Exception)
        {
            return "invalid";
        }
    }
}
