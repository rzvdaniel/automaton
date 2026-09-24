using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Automaton.Api.Services;

public static class RunnerHostRuntimeAddressValidator
{
    private static readonly (uint Network, int PrefixLength)[] NonPublicRanges =
    [
        (0x00000000, 8),  // 0.0.0.0/8
        (0x0A000000, 8),  // 10.0.0.0/8
        (0x7F000000, 8),  // 127.0.0.0/8
        (0x64400000, 10), // 100.64.0.0/10
        (0xA9FE0000, 16), // 169.254.0.0/16
        (0xAC100000, 12), // 172.16.0.0/12
        (0xC0000000, 24), // 192.0.0.0/24
        (0xC0000200, 24), // 192.0.2.0/24
        (0xC0586300, 24), // 192.88.99.0/24
        (0xC0A80000, 16), // 192.168.0.0/16
        (0xC6120000, 15), // 198.18.0.0/15
        (0xC6336400, 24), // 198.51.100.0/24
        (0xCB007100, 24), // 203.0.113.0/24
        (0xE0000000, 3)   // 224.0.0.0/3 and above
    ];

    public static bool TryValidatePublicIpv4(string? value, out string error)
    {
        error = string.Empty;
        if (!IPAddress.TryParse(value?.Trim(), out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "A valid public IPv4 address is required for public host-port connectivity.";
            return false;
        }

        if (IsInNonPublicRange(BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes())))
        {
            error = "The public host-port address must be a publicly routable unicast IPv4 address.";
            return false;
        }

        return true;
    }

    private static bool IsInNonPublicRange(uint address)
    {
        foreach (var (network, prefixLength) in NonPublicRanges)
        {
            var mask = uint.MaxValue << (32 - prefixLength);
            if ((address & mask) == network)
                return true;
        }

        return false;
    }
}
