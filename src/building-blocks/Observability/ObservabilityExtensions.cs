using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;

namespace ECommerce.Observability;

/// <summary>
/// One-call observability setup: structured logging, distributed tracing, and metrics, configured identically in
/// every service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Structured logging, distributed tracing, the three pillars of observability.
/// See <c>docs/operations/observability.md</c>.
/// </para>
/// <para>
/// <b>Why this is a shared building block rather than copied setup.</b> With nine services, configuration that
/// is copied will diverge — one service ends up with a different service-name convention, another omits an
/// enricher — and the moment they diverge, cross-service correlation quietly stops working. Since the entire
/// value of tracing is joining spans <i>across</i> services, "roughly the same everywhere" is worth much less
/// than "identical everywhere".
/// </para>
/// </remarks>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Adds Serilog and OpenTelemetry to the host.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="serviceName">Logical service name — appears as <c>service.name</c> on every span and metric,
    /// and is how you filter one service out of a shared trace backend. Keep it stable across restarts and
    /// versions.</param>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string serviceVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        AddSerilog(builder, serviceName);
        AddOpenTelemetry(builder, serviceName, serviceVersion);

        return builder;
    }

    private static void AddSerilog(WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, services, configuration) => configuration
            // Read levels and overrides from appsettings so log verbosity is changeable without a rebuild.
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()

            // Attaches TraceId and SpanId to every log event, which is what lets you pivot from a slow span in
            // Jaeger to exactly the log lines that span produced. Without it the two systems stay disconnected
            // and you are correlating by timestamp, which does not work under concurrency.
            .Enrich.WithSpan()
            .Enrich.WithProperty("ServiceName", serviceName)

            // ASP.NET Core emits one log line per request pipeline stage. Serilog's request logging replaces
            // that with a single enriched completion event per request - far less noise, more information.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            // InvariantCulture on both sinks, deliberately: logs are machine-parsed and read by operators in
            // many locales. A number formatted as "1,5" because the container happened to have a German locale
            // is a genuine and very confusing production bug.
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.Seq(
                serverUrl: context.Configuration["Observability:SeqUrl"] ?? "http://seq:5341",
                apiKey: context.Configuration["Observability:SeqApiKey"],
                formatProvider: CultureInfo.InvariantCulture));
    }

    private static void AddOpenTelemetry(WebApplicationBuilder builder, string serviceName, string serviceVersion)
    {
        string otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"] ?? "http://jaeger:4317";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health checks fire every few seconds and would otherwise dominate the trace backend with
                    // spans nobody will ever read.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation()

                // Custom ActivitySources: the outbox dispatcher and the event-bus consumer create spans by hand
                // so that an order can be followed across an asynchronous hop. See ActivitySources below.
                .AddSource(ActivitySources.EventBus)
                .AddSource(ActivitySources.Outbox)
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)))

            // Ship logs over OTLP as well as to Seq. Seq is the better developer experience for querying; OTLP
            // keeps logs, traces and metrics in one backend, which is where this would go in production.
            .WithLogging(logging => logging.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));
    }

    /// <summary>
    /// Adds the observability middleware. Call early in the pipeline — see the ordering note below.
    /// </summary>
    /// <remarks>
    /// <b>Order is load-bearing.</b> Correlation must come first so that a request failing in exception handling
    /// is still correlated; request logging comes next so the completion event carries the correlation id.
    /// Getting this backwards produces logs that look fine and cannot be joined.
    /// </remarks>
    public static WebApplication UseObservability(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseCorrelationId();
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());

                if (httpContext.Items.TryGetValue(CorrelationId.ItemsKey, out object? correlationId)
                    && correlationId is not null)
                {
                    diagnosticContext.Set(CorrelationId.LogPropertyName, correlationId);
                }
            };
        });

        return app;
    }
}

/// <summary>
/// Names of the custom <see cref="System.Diagnostics.ActivitySource"/>s this system creates spans on.
/// </summary>
/// <remarks>
/// Automatic instrumentation covers incoming HTTP and outgoing <c>HttpClient</c> calls, but it cannot see across
/// a message broker — from the publisher's point of view the work ends at publish, and from the consumer's it
/// begins from nothing. These sources are where the trace is stitched back together by hand, using the
/// <c>traceparent</c> carried on the message.
/// </remarks>
public static class ActivitySources
{
    public const string EventBus = "ECommerce.EventBus";
    public const string Outbox = "ECommerce.Outbox";
}
