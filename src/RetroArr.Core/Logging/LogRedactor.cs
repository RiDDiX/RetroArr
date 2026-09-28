using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RetroArr.Core.Logging
{
    public static class LogRedactor
    {
        // sig = signed download links; r = Newznab's API key parameter (only as a whole parameter name)
        private static readonly Regex ApiKeyInUrl = new(
            @"(apikey|apiKey|api_key|key|token|secret|password|passwd|client_id|client_secret|refresh_token|devid|devpassword|ssid|sspassword|sig|(?<=[?&])r)=([^&\s""'<>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> SensitiveHeaders = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "Cookie", "X-Api-Key", "X-Api-Token",
            "Set-Cookie", "Proxy-Authorization"
        };

        public static string RedactUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return ApiKeyInUrl.Replace(url, "$1=[REDACTED]");
        }

        // Download links carry indexer API keys (Prowlarr apikey=, Jackett jackett_apikey=,
        // Newznab r=) and tracker passkeys, in the query or the path. The log only gets the
        // host and the release name.
        public static string DescribeDownloadUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "magnet"))
                return RedactUrl(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var name = query["file"] ?? query["dn"];
            var source = uri.Scheme == "magnet" ? "magnet link" : uri.Host;
            return string.IsNullOrEmpty(name) ? source : $"'{name}' from {source}";
        }

        public static string RedactHeaderValue(string headerName, string headerValue)
        {
            if (SensitiveHeaders.Contains(headerName))
                return "[REDACTED]";
            return headerValue;
        }

        public static string Redact(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            return ApiKeyInUrl.Replace(message, "$1=[REDACTED]");
        }
    }
}
