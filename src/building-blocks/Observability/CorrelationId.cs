using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace ECommerce.Observability;

/// <summary>
/// Constants for the correlation identifier carried across every service and every asynchronous hop.
/// </summary>
public static class CorrelationId
{
    /// <summary>The HTTP header carrying the correlation id, in and out.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>The structured-log property name. Matches the header so a Seq query needs no translation.</summary>
    public const string LogPropertyName = "CorrelationId";

    /// <summary>Key under which the id is stashed in <see cref="HttpContext.Items"/> for the current request.</summary>
    public const string ItemsKey = "CorrelationId";
}

/// <summary>
/// Assigns or adopts a correlation id for every request, puts it in the logging context, and echoes it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Correlation ID propagation. See <c>docs/operations/observability.md</c>.
/// </para>
/// <para>
/// <b>Why this exists alongside distributed tracing.</b> OpenTelemetry already produces a trace id, so this can
/// look redundant. It is not, for two reasons. First, traces are commonly <i>sampled</i> — under load you keep a
/// percentage — whereas logs are usually kept in full; a correlation id present on every log line still ties a
/// request together when its trace was sampled away. Second, a correlation id is something a human can be given:
/// it goes in an error response, a customer quotes it to support, and one query finds everything that happened.
/// A W3C trace id is not something you read down the phone.
/// </para>
/// <para>
/// <b>Inbound ids are adopted, not overwritten.</b> If a caller already sent one — a BFF forwarding a browser
/// request — reusing it is what makes the chain traceable end to end. Generating a fresh id at each hop produces
/// several disconnected fragments of one user action, which is the failure this middleware exists to prevent.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers.TryGetValue(CorrelationId.HeaderName, out var incoming)
                               && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Guid.CreateVersion7().ToString();

        context.Items[CorrelationId.ItemsKey] = correlationId;

        // Echo it before the response starts. Registering on OnStarting rather than setting it after `next`
        // matters: once the response has begun, headers are immutable and setting one throws.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Everything logged inside this scope - including from code with no idea this middleware exists -
        // carries the property. That ambient behaviour is the whole value.
        using (LogContext.PushProperty(CorrelationId.LogPropertyName, correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>Registration helper for <see cref="CorrelationIdMiddleware"/>.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    /// Adds correlation-id handling. Register this <b>first</b>, before exception handling and logging, so that
    /// even a request that fails immediately is still correlated.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
