using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using RetroArr.Core.Configuration;

namespace RetroArr.Api.V3.Auth
{
    public class ApiKeyAuthMiddleware
    {
        public const string HeaderName = "X-Api-Key";
        public const string QueryName = "apiKey";
        public const string AccessTokenQueryName = "access_token";

        private readonly RequestDelegate _next;
        private readonly ApiKeyService _apiKeyService;

        public ApiKeyAuthMiddleware(RequestDelegate next, ApiKeyService apiKeyService)
        {
            _next = next;
            _apiKeyService = apiKeyService;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!RequiresAuth(context))
            {
                await _next(context);
                return;
            }

            var configured = _apiKeyService.GetApiKey();
            var presented = ResolvePresentedKey(context);

            if (!string.IsNullOrEmpty(presented) && FixedTimeEquals(presented, configured))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = "ApiKey";
            await context.Response.WriteAsync("{\"error\":\"Missing or invalid API key.\"}");
        }

        private static bool RequiresAuth(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
            var isHub = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
            if (!isApi && !isHub) return false;

            // Bootstrap endpoint is only reachable from loopback; guarded inside the controller.
            if (path.Equals("/api/v3/system/apikey/bootstrap", StringComparison.OrdinalIgnoreCase))
                return false;

            // Docker healthcheck hits this over loopback with no key.
            if (path.Equals("/api/v3/system/status", StringComparison.OrdinalIgnoreCase))
                return false;

            // Emulator assets + player html + rom stream get loaded by
            // <script>, iframe, and fetch — can't add an api key header
            // on those, so let them through.
            if (path.StartsWith("/api/v3/emulator/assets/", StringComparison.OrdinalIgnoreCase))
                return false;
            if (path.StartsWith("/api/v3/emulator/player", StringComparison.OrdinalIgnoreCase))
                return false;
            if (path.StartsWith("/api/v3/emulator/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/rom", StringComparison.OrdinalIgnoreCase))
                return false;

            if (IsLoopback(context))
                return false;

            return true;
        }

        private static string? ResolvePresentedKey(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrEmpty(header))
                return header.ToString();
            if (context.Request.Query.TryGetValue(QueryName, out var query) && !string.IsNullOrEmpty(query))
                return query.ToString();
            if (context.Request.Query.TryGetValue(AccessTokenQueryName, out var accessToken) && !string.IsNullOrEmpty(accessToken))
                return accessToken.ToString();
            if (context.Request.Headers.TryGetValue("Authorization", out var auth) && !string.IsNullOrEmpty(auth))
            {
                var value = auth.ToString();
                const string bearer = "Bearer ";
                if (value.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(bearer.Length);
            }
            return null;
        }

        private static bool IsLoopback(HttpContext context)
        {
            // A proxy terminating on loopback would bypass auth entirely
            // without this check — X-Forwarded-For means the real caller
            // is remote, so treat it as such.
            if (context.Request.Headers.ContainsKey("X-Forwarded-For")
                || context.Request.Headers.ContainsKey("Forwarded"))
                return false;

            var ip = context.Connection.RemoteIpAddress;
            if (ip == null) return true;
            return IPAddress.IsLoopback(ip);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
            var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
