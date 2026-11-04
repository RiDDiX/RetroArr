using System;
using System.Globalization;
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
        public static readonly TimeSpan HubTicketLifetime = TimeSpan.FromMinutes(5);

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
            var presented = ResolvePresentedKey(context, isHub);
            var keyValid = !string.IsNullOrEmpty(presented) && FixedTimeEquals(presented, configured);

            if (keyValid || LocalRequest.IsLocal(context) || HasValidSignature(context, configured)
                || (isHub && HasValidHubTicket(context, presented, configured)))
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

        // SignalR's websocket can only carry its token in the query, where proxy access logs keep it,
        // so the hubs take a short-lived ticket there (long polling sends the same one as Bearer).
        private static bool HasValidHubTicket(HttpContext context, string? presented, string apiKey)
        {
            var now = DateTimeOffset.UtcNow;
            return IsValidHubTicket(context.Request.Query[AccessTokenQueryName].ToString(), apiKey, now)
                || IsValidHubTicket(presented, apiKey, now);
        }

        // "<exp>.<sig>", keyed like SignedUrl, so rotating the key revokes it too
        public static string HubTicket(string apiKey, DateTimeOffset now)
        {
            var exp = (now.ToUnixTimeSeconds() + (long)HubTicketLifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return exp + "." + SignedUrl.Compute("hub:" + exp, apiKey);
        }

        public static bool IsValidHubTicket(string? ticket, string apiKey, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(apiKey)) return false;
            var dot = ticket.IndexOf('.');
            if (dot < 0
                || !long.TryParse(ticket.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var exp)
                || exp < now.ToUnixTimeSeconds())
                return false;
            return FixedTimeEquals(ticket[(dot + 1)..], SignedUrl.Compute("hub:" + ticket[..dot], apiKey));
        }

        private static string? ResolvePresentedKey(HttpContext context, bool isHub)
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrEmpty(header))
                return header.ToString();
            // On the hubs the query only carries a ticket (HasValidHubTicket)
            if (!isHub && context.Request.Query.TryGetValue(QueryName, out var query) && !string.IsNullOrEmpty(query))
                return query.ToString();
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
