using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using RetroArr.Core.Logging;

namespace RetroArr.Host.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Read or generate request ID
            var requestId = context.Request.Headers["X-Request-Id"].ToString();
            if (string.IsNullOrEmpty(requestId))
            {
                requestId = Guid.NewGuid().ToString("N")[..12];
            }

            // Propagate to CorrelationContext and NLog ScopeContext
            CorrelationContext.RequestId = requestId;

            // Echo back to client
            context.Response.Headers["X-Request-Id"] = requestId;
            context.Items["RequestId"] = requestId;

            using (NLog.ScopeContext.PushProperty("RequestId", requestId))
            {
                try
                {
                    await _next(context);
                }
                finally
                {
                    CorrelationContext.RequestId = null;
                }
            }
        }
    }
}
