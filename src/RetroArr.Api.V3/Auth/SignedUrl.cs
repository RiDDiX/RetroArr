using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace RetroArr.Api.V3.Auth
{
    // Short-lived links for things the browser loads without our headers (the emulator's ROM
    // fetch, <img>/<video>, download links), so the API key doesn't have to go into the URL.
    // The signature covers path and query and is keyed with the API key, so rotating the key
    // revokes every link.
    public static class SignedUrl
    {
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(6);
        private const string SigParam = "&sig=";

        // '/api/v3/emulator/5/rom' -> '/api/v3/emulator/5/rom?exp=1790000000&sig=...'
        // The expiry is rounded up to a whole window, so the link stays the same within a window
        // (EmulatorJS caches ROMs by URL) and is valid for one to two lifetimes.
        public static string Sign(string url, string apiKey, DateTimeOffset now, TimeSpan? lifetime = null)
        {
            var window = (long)(lifetime ?? DefaultLifetime).TotalSeconds;
            var exp = (now.ToUnixTimeSeconds() / window + 2) * window;
            var unsigned = url + (url.Contains('?') ? "&" : "?") + "exp=" + exp.ToString(CultureInfo.InvariantCulture);
            return unsigned + SigParam + Compute(unsigned, apiKey);
        }

        public static bool IsValid(HttpRequest request, string apiKey, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(apiKey)) return false;
            var query = request.QueryString.Value ?? string.Empty;
            var at = query.LastIndexOf(SigParam, StringComparison.Ordinal);
            if (at < 0) return false;

            // exp was part of what got signed; a repeated exp reads as "a,b" and fails to parse
            if (!long.TryParse(request.Query["exp"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var exp)
                || exp < now.ToUnixTimeSeconds())
                return false;

            var expected = Compute(request.Path.ToUriComponent() + query[..at], apiKey);
            var presented = query[(at + SigParam.Length)..];
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(presented), Encoding.ASCII.GetBytes(expected));
        }

        private static string Compute(string pathAndQuery, string apiKey)
        {
            var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes("RetroArr.SignedUrl.v1:" + apiKey), Encoding.UTF8.GetBytes(pathAndQuery));
            return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
