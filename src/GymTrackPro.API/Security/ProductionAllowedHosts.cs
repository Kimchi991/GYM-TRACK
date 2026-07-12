using System.Net;

namespace GymTrackPro.API.Security;

public static class ProductionAllowedHosts
{
    public static bool IsValid(string? configuredHosts)
    {
        if (string.IsNullOrWhiteSpace(configuredHosts))
        {
            return false;
        }

        var hosts = configuredHosts.Split(';', StringSplitOptions.None);
        if (hosts.Length == 0 || hosts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        foreach (var configuredHost in hosts)
        {
            var host = configuredHost.Trim();
            if (host.Contains('*', StringComparison.Ordinal)
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || host.Contains("://", StringComparison.Ordinal)
                || host.Contains('/')
                || host.Contains('\\'))
            {
                return false;
            }

            var addressText = host.Length > 2 && host[0] == '[' && host[^1] == ']'
                ? host[1..^1]
                : host;
            if (IPAddress.TryParse(addressText, out var address))
            {
                if (address.IsIPv4MappedToIPv6)
                {
                    address = address.MapToIPv4();
                }

                if (IPAddress.IsLoopback(address)
                    || address.Equals(IPAddress.Any)
                    || address.Equals(IPAddress.IPv6Any))
                {
                    return false;
                }

                continue;
            }

            if (Uri.CheckHostName(host) != UriHostNameType.Dns)
            {
                return false;
            }
        }

        return true;
    }
}
