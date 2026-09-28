using System.Net;
using Microsoft.AspNetCore.Http;

namespace RetroArr.Api.V3.Auth
{
    // Decides whether a request comes from this machine. UseForwardedHeaders replaces
    // Connection.RemoteIpAddress with the X-Forwarded-For value and drops the header it
    // consumed, so the socket peer has to be recorded before that middleware runs.
    public static class LocalRequest
    {
        public const string SocketPeerItem = "RetroArr.SocketPeer";

        // X-Original-* are set by ForwardedHeadersMiddleware when it rewrites the request.
        private static readonly string[] ProxyHeaders =
        {
            "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "Forwarded", "X-Real-IP",
            "X-Original-For", "X-Original-Host", "X-Original-Proto", "Via", "CF-Connecting-IP", "True-Client-IP"
        };

        public static void CaptureSocketPeer(HttpContext context)
        {
            context.Items[SocketPeerItem] = context.Connection.RemoteIpAddress;
        }

        public static bool IsLocal(HttpContext context)
        {
            // Without the recorded peer there is nothing trustworthy to decide on
            if (!context.Items.TryGetValue(SocketPeerItem, out var value) || value is not IPAddress peer)
                return false;
            if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
            if (!IPAddress.IsLoopback(peer)) return false;

            // A reverse proxy on the same host connects over loopback too
            foreach (var header in ProxyHeaders)
            {
                if (context.Request.Headers.ContainsKey(header)) return false;
            }

            // A browser can be steered at the loopback port by another site: DNS rebinding
            // leaves the attacker's name in Host, a cross-site request says so in Sec-Fetch-Site.
            var host = (context.Request.Host.Host ?? string.Empty).Trim('[', ']');
            var hostIsLoopback = host.Equals("localhost", System.StringComparison.OrdinalIgnoreCase)
                                 || (IPAddress.TryParse(host, out var hostAddress) && IPAddress.IsLoopback(hostAddress));
            if (!hostIsLoopback) return false;

            var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
            return fetchSite.Length == 0 || fetchSite == "same-origin" || fetchSite == "none";
        }
    }
}
