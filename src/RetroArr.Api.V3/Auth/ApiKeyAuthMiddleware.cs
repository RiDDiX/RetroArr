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
        // Set for requests that passed the key check (or came from this machine), also on the
        // anonymous routes, so those can decide how much to show.
        public const string AuthenticatedItem = "RetroArr.Authenticated";
        public const string StatusPath = "/api/v3/system/status";

        private readonly RequestDelegate _next;
        private readonly ApiKeyService _apiKeyService;

        public ApiKeyAuthMiddleware(RequestDelegate next, ApiKeyService apiKeyService)
        {
            _next = next;
            _apiKeyService = apiKeyService;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
            var isHub = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
            if (!isApi && !isHub)
            {
                await _next(context);
                return;
            }

            var configured = _apiKeyService.GetApiKey();
            var presented = ResolvePresentedKey(context);
            var keyValid = !string.IsNullOrEmpty(presented) && FixedTimeEquals(presented, configured);

            if (keyValid || LocalRequest.IsLocal(context) || HasValidSignature(context, configured))
            {
                context.Items[AuthenticatedItem] = true;
                await _next(context);
                return;
            }

            // A wrong key on the status route is refused, so the key check in the UI can tell
            if (IsAnonymous(path) && !(presented != null && path.Equals(StatusPath, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = "ApiKey";
            await context.Response.WriteAsync("{\"error\":\"Missing or invalid API key.\"}");
        }

        private static bool IsAnonymous(string path)
        {
            // Docker healthcheck; it only gets a minimal answer without a key.
            if (path.Equals(StatusPath, StringComparison.OrdinalIgnoreCase))
                return true;

            // EmulatorJS files and the player page are loaded by <script> and the iframe,
            // which can't send the key. The player gets a signed ROM link from the app.
            if (path.StartsWith("/api/v3/emulator/assets/", StringComparison.OrdinalIgnoreCase))
                return true;
            if (path.Equals("/api/v3/emulator/player", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool HasValidSignature(HttpContext context, string apiKey)
        {
            var method = context.Request.Method;
            if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) return false;
            return SignedUrl.IsValid(context.Request, apiKey, DateTimeOffset.UtcNow);
        }

        private static string? ResolvePresentedKey(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrEmpty(header))
                return header.ToString();
            if (context.Request.Query.TryGetValue(QueryName, out var query) && !string.IsNullOrEmpty(query))
                return query.ToString();
            // SignalR's websocket can only carry the key in the query; nothing else needs that name
            if (context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
                && context.Request.Query.TryGetValue(AccessTokenQueryName, out var accessToken) && !string.IsNullOrEmpty(accessToken))
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

        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
            var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
