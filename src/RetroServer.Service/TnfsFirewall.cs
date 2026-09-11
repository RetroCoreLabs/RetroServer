//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen 
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System.Net;
using RetroIO.Security.Firewall;

namespace RetroServer.Service;

public sealed class TnfsFirewall
{
    private readonly FirewallConfiguration _config;

    public TnfsFirewall(FirewallConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool IsAllowed(string ipAddress)
    {
        if (!_config.Enabled)
        {
            return true;
        }

        if (!IPAddress.TryParse(ipAddress, out var ip))
        {
            return false; // Invalid IP address
        }

        // Check blocked list first (highest priority)
        if (_config.BlockedIpAddresses.Contains(ipAddress))
        {
            return false;
        }

        // Check allowed list
        if (_config.AllowedIpAddresses.Contains(ipAddress))
        {
            return true;
        }

        // Check localhost
        if (_config.AllowLocalhost && IsLocalhost(ip))
        {
            return true;
        }

        // Check private networks
        if (_config.AllowPrivateNetworks && IsPrivateNetwork(ip))
        {
            return true;
        }

        // Check allowed networks (CIDR)
        for (int i = 0; i < _config.AllowedNetworks.Count; i++)
        {
            if (IsInNetwork(ip, _config.AllowedNetworks[i]))
            {
                return true;
            }
        }

        // Default deny if no rules matched
        return false;
    }

    private static bool IsLocalhost(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        return false;
    }

    private static bool IsPrivateNetwork(IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();

        // IPv4 private networks
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }
        }
        // IPv6 private networks
        else if (bytes.Length == 16)
        {
            // fc00::/7 (Unique Local Addresses)
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInNetwork(IPAddress ip, string cidr)
    {
        string[] parts = cidr.Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out var networkAddress))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int prefixLength))
        {
            return false;
        }

        byte[] ipBytes = ip.GetAddressBytes();
        byte[] networkBytes = networkAddress.GetAddressBytes();

        if (ipBytes.Length != networkBytes.Length)
        {
            return false; // IPv4 vs IPv6 mismatch
        }

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        // Check full bytes
        for (int i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != networkBytes[i])
            {
                return false;
            }
        }

        // Check remaining bits
        if (remainingBits > 0)
        {
            byte mask = (byte)(0xFF << (8 - remainingBits));
            if ((ipBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }
}
