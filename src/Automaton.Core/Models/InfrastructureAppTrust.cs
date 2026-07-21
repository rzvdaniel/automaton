namespace Automaton.Core.Models;

public static class InfrastructureAppTrust
{
    public static readonly Guid PlatformAppId = Guid.Parse("7a33f453-f04a-4d17-b767-8c94d9593d30");
    public const string PlatformAppName = "Automaton Infrastructure";

    public static bool IsTrustedApp(Guid appId) => appId == PlatformAppId;
}
