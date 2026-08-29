using System.Diagnostics;
using Serilog.Context;

namespace QuotesApi.Middleware;

// Pushes the per-request trace id into Serilog's LogContext so every log line written
// while handling this request - including ones from ExceptionMiddleware - is enriched
// with a TraceId property. Must run before ExceptionMiddleware so exception logs carry
// the id too.
//
// The id must be Activity.Current's W3C trace id, not HttpContext.TraceIdentifier:
// once OpenTelemetry's ASP.NET Core instrumentation is active, Serilog's console
// template renders its own built-in {TraceId} token from Activity.Current, ignoring
// any same-named LogContext property. Pushing TraceIdentifier here used to leave the
// X-Trace-Id header (and this property) holding a different value than what a human
// actually sees on the console - a customer handed the header id couldn't grep the
// log for it. Falls back to TraceIdentifier only when no Activity exists.
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.Response.Headers["X-Trace-Id"] = traceId;

        using (LogContext.PushProperty("TraceId", traceId))
        {
            await _next(context);
        }
    }
}
