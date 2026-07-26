# Observability

> **Code:** [`ObservabilityExtensions.cs`](../../src/building-blocks/Observability/ObservabilityExtensions.cs) ·
> [`CorrelationId.cs`](../../src/building-blocks/Observability/CorrelationId.cs)

Nine services means an order that fails somewhere is undebuggable without this. Observability is therefore
built in Phase 1, before there is anything to observe — retrofitting it means retrofitting it into code
written without it, which is much harder and always partial.

## Configured identically everywhere, on purpose

One shared building block rather than per-service setup. With nine services, copied configuration diverges —
one ends up with a different service-name convention, another omits an enricher — and the moment they
diverge, cross-service correlation stops working. Since the entire value is joining signals *across*
services, "roughly the same everywhere" is worth far less than "identical everywhere".

## The three signals, and where they go

| Signal | Produced by | Shipped to | Open at |
|--------|-------------|-----------|---------|
| **Logs** | Serilog | Seq (query UX) **and** OTLP | http://localhost:8081 |
| **Traces** | OpenTelemetry | Jaeger via OTLP | http://localhost:16686 |
| **Metrics** | OpenTelemetry | OTLP | — |

Logs go to both deliberately: Seq is a far better developer experience for querying structured logs, while
OTLP keeps all three signals in one backend, which is where this would go in production.

## Correlation ID and trace ID — why both

They look redundant. They are not:

- **Traces are sampled.** Under load you keep a percentage. Logs are usually kept in full. A correlation id on
  every log line still ties a request together when its trace was sampled away.
- **A correlation id is human-portable.** It goes in an error response, a customer quotes it to support, and
  one query finds everything. Nobody reads a W3C trace id down the phone.

The id is **adopted, not regenerated**. If a caller already sent one — a BFF forwarding a browser request —
reusing it is what makes the chain traceable end to end. Generating a fresh one at each hop produces several
disconnected fragments of a single user action, which is precisely the failure this exists to prevent.

Verified in Phase 1:

```bash
curl -i -H "X-Correlation-Id: my-trace" http://localhost:5001/
# → X-Correlation-Id: my-trace
```

Serilog's `WithSpan` enricher attaches `TraceId` and `SpanId` to every log event, which is what lets you pivot
from a slow span in Jaeger straight to the log lines that span produced. Without it the two systems stay
disconnected and you are correlating by timestamp — which does not work under concurrency.

## The hard part: tracing across the broker

Automatic instrumentation covers incoming HTTP and outgoing `HttpClient` calls. It **cannot see across a
message broker** — from the publisher's side the work ends at publish; from the consumer's side it begins from
nothing. Left alone, every event boundary breaks the trace, which in an event-driven system means the trace
breaks everywhere that matters.

The fix, wired through Phase 1's plumbing and completed in Phase 6:

1. The publisher writes the current W3C `traceparent` onto the outbox row.
2. The event carries it in `IntegrationEvent.TraceParent` and as a RabbitMQ message header.
3. The consumer starts its span with that as the parent, on the `ECommerce.EventBus` `ActivitySource`.

The result is one trace covering *order placed → stock reserved → payment taken → order confirmed → email
sent*, across five services and four asynchronous hops.

## Worked example — tracing one order

_Completed in Phase 7, once there is an order to trace._ The shape:

1. Place an order in the storefront; the response carries `X-Correlation-Id`.
2. In Seq: `CorrelationId = '<id>'` returns every log line from every service, in order.
3. In Jaeger: search by that trace id for the waterfall — which service was slow, where the saga waited, which
   span carried the error.
4. Correlate the two: a span that took 2 s, and the log lines it emitted, side by side.

## What Phase 12 adds

- Per-service dependency health checks feeding readiness
- RED metrics (rate, errors, duration) per endpoint, and business metrics (orders placed, payments declined)
- Sampling configuration — `always_on` is a development setting; production uses parent-based ratio sampling
- Log-level overrides per namespace, changeable without a redeploy
- Alerting rules, and what each one would actually mean
