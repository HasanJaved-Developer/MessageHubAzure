using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using OpenTelemetry.Trace; // <- this brings in Activity.RecordException(...)

namespace SharedLibrary.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context); // continue pipeline
            }
            catch (OperationCanceledException oce) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client aborted; don’t try to write a response body.
                _logger.LogWarning(oce, "Request aborted by client {Method} {Path} (StatusCode={StatusCode})",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response?.StatusCode);
                // Let it bubble or just return; here we just return.
            }
            catch (Exception ex)
            {

                var activity = Activity.Current;
                if (activity is not null)
                {
                    var tags = new ActivityTagsCollection
                    {
                        ["exception.type"] = ex.GetType().FullName,
                        ["exception.message"] = ex.Message,
                        ["exception.stacktrace"] = ex.ToString(),
                    };

                    // add the OpenTelemetry "exception" event to the current span
                    activity.AddEvent(new ActivityEvent("exception", tags: tags));

                    // mark the span as error so it shows red in Jaeger
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                }


                // Log using Serilog (through ILogger)
                _logger.LogError(ex, "Unhandled exception occurred while processing request {Path}", context.Request.Path);

                // If the server already committed headers/body, we can't change it.
                if (context.Response.HasStarted)
                {
                    _logger.LogWarning("The response has already started; cannot write error body. Rethrowing.");
                    throw; // Let server infrastructure terminate the connection appropriately.
                }

                // Clear headers/status that may have been set
                context.Response.Clear();
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;

                // If a previous component wrote to the (buffered) body, wipe it safely.
                try
                {
                    if (context.Response.Body.CanSeek)
                    {
                        context.Response.Body.SetLength(0);
                        context.Response.Body.Position = 0;
                    }
                }
                catch
                {
                    // If the body stream isn't seekable/writable (rare with your buffer), ignore.
                }
                // Let the server recalc Content-Length
                context.Response.Headers.ContentLength = null;
    

                var result = new
                {
                    error = "An unexpected error occurred",
                    details = ex.Message // optional, avoid exposing internals in production
                };

                await context.Response.WriteAsJsonAsync(result);
            }
        }
    }
}
