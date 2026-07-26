using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerce.Observability;

/// <summary>
/// Health-check conventions shared by every service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Health Endpoint Monitoring, with the liveness/readiness split.
/// See <c>docs/operations/health-checks.md</c>.
/// </para>
/// <para>
/// <b>The distinction that matters, and that most implementations get wrong.</b> These two endpoints answer
/// different questions and have opposite consequences:
/// </para>
/// <list type="table">
///   <item>
///     <term><c>/health/live</c> — liveness</term>
///     <description><i>Is this process irrecoverably broken?</i> Checks nothing external. Failing it means
///     <b>kill and restart me</b>.</description>
///   </item>
///   <item>
///     <term><c>/health/ready</c> — readiness</term>
///     <description><i>Can I serve traffic right now?</i> Checks dependencies — database, broker, cache.
///     Failing it means <b>stop sending me traffic, but leave me alone</b>.</description>
///   </item>
/// </list>
/// <para>
/// <b>Why conflating them is actively dangerous.</b> Put a database check in liveness, and when the database has
/// a brief outage every replica fails liveness at once and the orchestrator restarts all of them. Restarting
/// does not fix a database — so you have converted a recoverable dependency blip into a full outage plus a crash
/// loop, and the restarts add connection-storm load to a database that is already struggling. The rule:
/// <b>liveness must never check anything the process cannot fix by restarting.</b>
/// </para>
/// <para>
/// Kubernetes is not built here (<c>docs/architecture.md §10</c>), but Docker Compose consumes the same
/// endpoints for <c>healthcheck</c> and <c>depends_on: condition: service_healthy</c>, and getting the split
/// right now is what makes layering K8s on later a configuration exercise rather than a redesign.
/// </para>
/// </remarks>
public static class HealthCheckExtensions
{
    /// <summary>Tag marking a check as a readiness (dependency) check.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>Tag marking a check as a liveness (self) check.</summary>
    public const string LivenessTag = "live";

    /// <summary>
    /// Registers the always-passing self check that backs <c>/health/live</c>.
    /// </summary>
    /// <remarks>
    /// It looks pointless — a check that always returns healthy. It is not: reaching it at all proves the
    /// process is running, Kestrel is accepting connections, and the request pipeline executes. That is
    /// precisely, and only, what liveness should assert.
    /// </remarks>
    public static IHealthChecksBuilder AddDefaultHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LivenessTag]);

    /// <summary>
    /// Maps <c>/health/live</c> and <c>/health/ready</c>.
    /// </summary>
    public static WebApplication MapDefaultHealthChecks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LivenessTag),
            ResponseWriter = WriteResponseAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadinessTag),
            ResponseWriter = WriteResponseAsync,
        });

        return app;
    }

    /// <summary>
    /// Writes a JSON body naming each check, its status, and its duration.
    /// </summary>
    /// <remarks>
    /// The default writer returns only the aggregate status as plain text, which tells an operator that
    /// <i>something</i> is wrong but not what. Naming the failing check turns a health endpoint from an alarm
    /// into a diagnosis. Note that <c>description</c> is included but exception detail is not — a health
    /// endpoint is often reachable from outside and must not leak connection strings or stack traces.
    /// </remarks>
    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
            }),
        }));
    }
}
