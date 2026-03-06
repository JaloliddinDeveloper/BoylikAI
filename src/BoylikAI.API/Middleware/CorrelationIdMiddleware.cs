using Serilog.Context;

namespace BoylikAI.API.Middleware;

/// <summary>
/// Propagates X-Correlation-ID header through the request pipeline.
/// If the client sends one, it is reused; otherwise a new GUID is generated.
/// The ID is pushed onto the Serilog LogContext so every log line includes it.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        // Return the ID to the caller so they can correlate client-side logs
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Make it available to downstream code via HttpContext.Items
        context.Items[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var existing)
            && !string.IsNullOrWhiteSpace(existing))
        {
            // Sanitize to prevent log injection — allow only safe characters
            var raw = existing.ToString();
            return raw.Length <= 64 && raw.All(c => char.IsLetterOrDigit(c) || c == '-')
                ? raw
                : Guid.NewGuid().ToString();
        }

        return Guid.NewGuid().ToString();
    }
}
