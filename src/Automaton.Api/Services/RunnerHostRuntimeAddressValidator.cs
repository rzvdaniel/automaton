using System.Net;
using System.Net.Sockets;

namespace Automaton.Api.Services;

public static class RunnerHostRuntimeAddressValidator
{
    public static bool TryValidatePublicIpv4(string? value, out string error)
    {
        error = string.Empty;
        if (!IPAddress.TryParse(value?.Trim(), out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "A valid public IPv4 address is required for public host-port connectivity.";
            return false;
        }

        var bytes = address.GetAddressBytes();
        var isPrivateOrSpecialPurpose =
            bytes[0] is 0 or 10 or 127 ||
            (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
            (bytes[0] == 169 && bytes[1] == 254) ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
            (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||
            (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 198 && bytes[1] is 18 or 19) ||
            (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
            (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
            bytes[0] >= 224;

        if (isPrivateOrSpecialPurpose)
        {
            error = "The public host-port address must be a publicly routable unicast IPv4 address.";
            return false;
        }

        return true;
    }
}
