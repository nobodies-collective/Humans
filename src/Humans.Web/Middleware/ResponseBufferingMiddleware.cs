using Microsoft.AspNetCore.Http;

namespace Humans.Web.Middleware;

/// <summary>
/// Buffers HTML responses so that view rendering exceptions are caught
/// before any bytes are sent to the client. Without buffering, response
/// compression streams partial output and a mid-render exception produces
/// ERR_CONTENT_DECODING_FAILED instead of a proper error page.
///
/// Only buffers text/html responses — static assets stream normally.
/// </summary>
public class ResponseBufferingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ResponseBufferingMiddleware> _logger;

    public ResponseBufferingMiddleware(RequestDelegate next, ILogger<ResponseBufferingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only buffer non-static requests (HTML pages)
        var path = context.Request.Path.Value;
        if (path != null && (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("/_", StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled exception during response rendering for {Path}", context.Request.Path);
            context.Response.Body = originalBody;
            throw; // Let the exception handler middleware render the error page
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
