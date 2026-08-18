using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Observability;

/// <summary>
/// Rate limiting and security headers for the gateways - the two edges every request crosses.
/// </summary>
/// <remarks>
/// <para>
/// <b>Applied at the BFFs, not at every service.</b> The BFFs are the trust boundary the internet can
/// reach; the services behind them accept traffic only from the internal network. Limiting at the edge
/// sheds abusive load before it costs a proxied hop, and keeps eleven services from each owning a copy
/// of the same policy. (Authentication is the exception - validated at the edge AND at every service -
/// because identity is worth defending in depth in a way that request budgets are not.)
/// </para>
/// <para>
/// See [ADR-0022](../../../docs/adr/0022-edge-hardening-defaults.md) for the numbers and what they cost.
/// </para>
/// </remarks>
public static class EdgeHardening
{
    /// <summary>
    /// A fixed-window request budget per client IP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Partitioned by IP</b>, because that is the only identity an unauthenticated attacker has. A
    /// per-user partition would let one botnet of anonymous clients starve everyone; per-IP means each
    /// source pays for its own behaviour. The cost, stated honestly: clients behind one NAT share a
    /// budget, which is why the default is generous rather than tight.
    /// </para>
    /// <para>
    /// <b>Fixed window over sliding or token bucket</b>: it is the algorithm a reader can verify with a
    /// watch and a loop - N requests succeed, the N+1th gets 429, the next minute starts fresh. The
    /// sophisticated algorithms shave edge cases off boundaries; they do not change what a demo or a
    /// dashboard shows, and this repository optimises for being understood.
    /// </para>
    /// <para>
    /// <b>Health probes are exempt.</b> An orchestrator polling <c>/health</c> is load the platform
    /// itself generates; throttling it makes the limiter kill its own container.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        int permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 1000);
        int windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                PathString path = context.Request.Path;

                if (path.StartsWithSegments("/health") || path.StartsWithSegments("/alive"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        // No queue. A queued request holds a connection to wait for a window that may
                        // be seconds away; under attack that is a slot an honest client cannot have.
                        // An immediate 429 with Retry-After is cheaper for both sides.
                        QueueLimit = 0,
                    });
            });

            options.OnRejected = (context, _) =>
            {
                // Retry-After turns a rejection into an instruction. A bare 429 teaches a client
                // nothing except "try again immediately", which is the opposite of the goal.
                context.HttpContext.Response.Headers.RetryAfter =
                    windowSeconds.ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>
    /// Response headers every gateway answer should carry.
    /// </summary>
    /// <remarks>
    /// A deliberately short list, because these are API responses: <c>nosniff</c> stops a browser
    /// reinterpreting a JSON body as something executable, <c>DENY</c> makes framing an API response
    /// meaningless, and the referrer policy keeps tokens in URLs (which should not exist, but defence in
    /// depth) out of other sites' logs. The content-security work for the <i>pages</i> lives in the web
    /// apps' nginx config, where the pages are.
    /// </remarks>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            return next(context);
        });
}
