using Serilog.Context;
using StoreOS.Application.Abstractions;

namespace StoreOS.Api.Middleware;

public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor accessor)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
            && Guid.TryParseExact(incoming, "D", out _)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("D");

        accessor.CorrelationId = correlationId;
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
