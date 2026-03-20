using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RetroArr.Core.Logging
{
    public static class LogRedactor
    {
        private static readonly Regex ApiKeyInUrl = new(
            @"(apikey|apiKey|api_key|key|token|secret|password|passwd|client_id|client_secret|refresh_token|devid|devpassword|ssid|sspassword)=([^&\s]+)",
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
