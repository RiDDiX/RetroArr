using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace RetroArr.Api.V3.Auth
{
    public static class TrustedProxies
    {
        public static readonly string[] DefaultRanges = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128" };
        private static readonly string[] LoopbackRanges = { "127.0.0.0/8", "::1/128" };

        // Fills KnownNetworks from RETROARR_TRUSTED_PROXIES. ASP.NET Core accepts forwarded
        // headers from any address when both lists are empty, so they never end up empty here.
        public static void Apply(ForwardedHeadersOptions options, string? configured, Action<string> log)
        {
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            var ranges = string.IsNullOrWhiteSpace(configured)
                ? DefaultRanges
                : configured.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var range in ranges)
            {
                if (TryParse(range, out var network)) options.KnownNetworks.Add(network!);
                else log($"[ForwardedHeaders] ignoring invalid RETROARR_TRUSTED_PROXIES entry '{range}'");
            }

            if (options.KnownNetworks.Count == 0)
            {
                log("[ForwardedHeaders] no valid RETROARR_TRUSTED_PROXIES entry, trusting loopback only");
                foreach (var range in LoopbackRanges)
                {
                    TryParse(range, out var network);
                    options.KnownNetworks.Add(network!);
                }
            }
        }

        public static bool TryParse(string range, out AspNetIPNetwork? network)
        {
            network = null;
            var slash = range.IndexOf('/');
            var addressPart = slash >= 0 ? range[..slash] : range;
            if (!IPAddress.TryParse(addressPart, out var address)) return false;

            var maxBits = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            var bits = maxBits;
            if (slash >= 0 && (!int.TryParse(range[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out bits) || bits > maxBits))
                return false;

            network = new AspNetIPNetwork(address, bits);
            return true;
        }
    }
}
