# ADR-0007: gRPC for internal synchronous calls, REST at the edge

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

Most communication in this system is asynchronous — integration events over RabbitMQ. But a small number of
calls genuinely cannot be deferred, because a caller is blocked on the answer:

- Basket → Catalog: *is this price still current?* (needed to render a correct cart, now)
- Ordering → Catalog: *snapshot the name and price* (needed at the moment of checkout)
- Notification → User Profile: *which channels has this user opted into?* (needed to dispatch)
- Every BFF → services (a user is waiting on a screen)

The BFF-to-service and browser-to-BFF hops are settled: browsers speak HTTP/JSON, so the edge is REST. The
open question is what the **internal service-to-service** calls use.

## Options considered

### Option A — REST/JSON everywhere
One protocol, uniform tooling, `curl`-debuggable, no code generation step.

The costs are real but subtle: the contract is a document (OpenAPI) that nothing forces you to obey, so
drift between a client's assumptions and a server's behaviour is caught at runtime rather than compile time.
JSON is verbose and comparatively slow to serialise, and per-call HTTP/1.1 connection handling adds overhead
on hot internal paths.

### Option B — gRPC internally, REST at the edge
Protobuf over HTTP/2 between services; REST/JSON from BFF outward.

### Option C — Make everything asynchronous, no synchronous internal calls
Purest decoupling: every service keeps a local replica of what it needs, fed by events, and never asks
anyone anything.

Genuinely worth considering, and correct for some of these cases — Notification's preference lookup is a
strong candidate for a local replica. Rejected as a blanket rule because it forces eventual consistency onto
data that must be exactly right *at that instant*. The price a customer is charged must be correct at
checkout, not eventually correct. Replicating Catalog's entire price book into Ordering to avoid one RPC
trades a small, well-understood coupling for a large, permanently-stale dataset.

## Decision

**gRPC for internal synchronous service-to-service calls. REST/JSON from the BFFs outward. Events for
everything that does not need an immediate answer.**

### Why gRPC internally

- **The contract is compiler-enforced.** A `.proto` file generates both client and server types. Removing a
  field breaks the build rather than a production request. At the edge this rigidity is a liability; between
  services it is exactly what you want.
- **Payload size and serialisation cost.** Protobuf is binary and materially smaller and faster than JSON.
  On a hot path like per-cart-line price validation, that compounds.
- **HTTP/2 multiplexing.** Many concurrent calls share one connection, avoiding connection-pool pressure
  between chatty services.
- **Streaming**, if a future case needs it (bulk catalog sync), without inventing a protocol.
- **First-class in .NET.** `Grpc.AspNetCore` integrates with DI, `HttpClientFactory`, Polly, health checks,
  and OpenTelemetry — the same building blocks used everywhere else.

### Why REST stays at the edge

Browsers cannot speak gRPC natively — gRPC-Web needs a proxy and still loses features. Beyond that, REST is
genuinely *better* at the edge: cacheable by intermediaries, debuggable with `curl` and browser devtools,
versionable through content negotiation, and readable in a network tab by a frontend developer who should
not have to install tooling to see what came back. The looseness that is a liability internally is a
feature externally.

**The boundary is the BFF**, and that is the natural place for it — the BFF already exists to translate
between service-shaped and client-shaped data.

### The rule for choosing at all

Before reaching for either, the question is whether the call should exist:

1. **Can this be an event?** If A is *telling* B something, it is an event. Default answer.
2. **Is the caller blocked on the answer?** If not, it is an event.
3. **Must the answer be authoritative right now?** If a slightly stale local copy would do, replicate via
   events instead.
4. Only if 1–3 all fail is it a synchronous call — and then gRPC internally, REST at the edge.

The full call inventory is in [architecture.md §6](../architecture.md#6-communication-choosing-synchronous-or-asynchronous).
There are deliberately very few entries.

## Consequences

### What this buys us
- Breaking a cross-service contract fails the build, not production.
- Lower latency and bandwidth on internal hot paths.
- Generated clients — no hand-written HTTP plumbing, no hand-maintained DTOs per consumer.
- `.proto` files are compact, readable, reviewable contracts that double as documentation.

### What this costs us
- **Two protocols to run and reason about.** Every service that exposes both needs two Kestrel endpoints,
  two health checks, and two sets of auth wiring.
- **A second port per service.** Kestrel cannot multiplex HTTP/1.1 and HTTP/2 on the same *plaintext* port,
  and TLS between containers is out of scope here, so gRPC gets its own (`REST port + 100`). Behind TLS in
  production both share 443. This is an artefact of the local setup, not of gRPC, and it is worth being able
  to say so.
- **Debugging is harder.** `curl` does not speak gRPC; you need `grpcurl` or Postman's gRPC support. Binary
  payloads are not human-readable on the wire.
- **A build step.** `.proto` compilation must run before the C# compiles, which complicates the build
  slightly and can produce confusing errors when a proto is malformed.
- **Versioning discipline.** Protobuf tolerates additive change well, but field numbers are permanent —
  reusing a retired field number is a silent, data-corrupting bug rather than a loud failure.

### What we will have to revisit
If the internal call count stays as low as it is now (four call sites), gRPC's benefits are modest and REST
would have been adequate. The honest position: **at this scale the choice is close**, and it is made partly
to demonstrate the pattern. In a system with hot internal fan-out, it would not be close.

## References

- [.NET microservices guide — communication in a microservice architecture](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/communication-in-microservice-architecture)
- [gRPC for .NET](https://learn.microsoft.com/en-us/aspnet/core/grpc/)
- [architecture.md §6](../architecture.md#6-communication-choosing-synchronous-or-asynchronous) — the call inventory
