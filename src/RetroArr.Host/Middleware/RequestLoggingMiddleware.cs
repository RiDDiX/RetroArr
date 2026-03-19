using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using RetroArr.Core.Logging;

namespace RetroArr.Host.Middleware
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly Logger _logger = LogManager.GetLogger(AppLoggerService.HttpRequest);

        public RequestLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip static file requests
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/static") || path.StartsWith("/assets") || 
                path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".map") ||
                path.EndsWith(".ico") || path.EndsWith(".png") || path.EndsWith(".jpg") ||
                path.EndsWith(".svg") || path.EndsWith(".woff") || path.EndsWith(".woff2"))
            {
                await _next(context);
                return;
            }

            var sw = Stopwatch.StartNew();
            var method = context.Request.Method;
            var redactedPath = LogRedactor.RedactUrl(path + context.Request.QueryString);

            try
            {
                await _next(context);
                sw.Stop();

                var statusCode = context.Response.StatusCode;
                if (statusCode >= 400)
                {
                    _logger.Warn("{Method} {Path} -> {StatusCode} ({Duration}ms)",
                        method, redactedPath, statusCode, sw.ElapsedMilliseconds);
                }
                else
                {
                    _logger.Info("{Method} {Path} -> {StatusCode} ({Duration}ms)",
                        method, redactedPath, statusCode, sw.ElapsedMilliseconds);
                }
            }
            catch (System.Exception ex)
            {
                sw.Stop();
                _logger.Error(ex, "{Method} {Path} -> 500 ({Duration}ms) {Error}",
                    method, redactedPath, sw.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }
    }
}
