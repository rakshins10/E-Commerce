# ADR-0006: YARP with a BFF per client family, not a single gateway

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

Five client applications need to reach nine services. Letting clients call services directly is untenable:
it hard-codes the service topology into every client, multiplies round trips (a product page needs catalog,
inventory, and pricing data), spreads CORS and authentication concerns across nine deployables, and makes
any refactoring of service boundaries a coordinated client release.

So there is an edge layer. The real question is **how many**, and what it is built with.

The clients are not alike. The storefront is public, read-heavy, and wants one fat payload per page. The
admin panel is permission-gated on every route, mutation-heavy, and needs audit logging. The mobile app is
latency- and battery-sensitive over an unreliable network and wants fewer, coarser calls.

## Options considered

### Option A — One gateway for everything
A single Ocelot or YARP instance routing all five clients to all nine services.

Operationally simplest: one thing to deploy, one place for cross-cutting concerns. It is also how most
systems start, and how they get into trouble.

It fails as a **coupling magnet**. Every client's needs accumulate in one codebase, so aggregation logic
grows conditionals (`if (client == "mobile")`), and a change the mobile app needs is deployed to a component
the admin panel depends on. One team's release cadence gates everyone's. Its threat model becomes the union
of all clients' threat models — the public storefront and the privileged admin surface share a process,
which is a poor security boundary. And it is a single point of failure and a single scaling unit: storefront
traffic spikes force you to scale the admin path too.

### Option B — A BFF per client family
One edge component per client experience, each owned by the team owning that client.

### Option C — GraphQL federation
One endpoint, each client queries exactly the shape it wants, no per-client backend needed.

Genuinely attractive for the over/under-fetching problem, and worth naming because it is the modern
alternative. Rejected here: it introduces a substantial new technology and a whole discipline (schema
federation, query cost analysis, persisted queries, N+1 resolution) that would dominate the repository and
obscure the patterns it exists to teach. It also does not remove the need for per-client concerns like the
admin audit log. Reconsidered in a real product if client data needs diverge faster than the BFFs can track.

## Decision

**Three BFFs — `storefront-bff`, `admin-bff`, `mobile-bff` — each built with YARP, each doing both reverse
proxying and purpose-built aggregation.**

| BFF | Serves | Character |
|-----|--------|-----------|
| `storefront-bff` | react-store, angular-store | Public, aggressively cached, aggregates product + stock + price into one product-page payload |
| `admin-bff` | react-admin, angular-admin | Every route permission-gated, every mutation audit-logged, fans out for administrative views |
| `mobile-bff` | rn-store | Fewer round trips, coarser payloads, more aggressive pagination |

### The deliberate asymmetry: React and Angular share a BFF

**A BFF exists per *client experience*, not per *framework*.** The React and Angular storefronts have
identical UX by requirement, therefore identical data needs, therefore one BFF. Splitting them would be pure
duplication.

This is not merely tidy — it is what makes parity *provable*. Both apps consume the same endpoints returning
the same payloads, so any behavioural difference is unambiguously a client-side bug rather than a backend
difference. The same reasoning underpins [ADR-0014](0014-react-and-angular-in-lockstep.md).

### Why YARP over Ocelot

Both are viable .NET reverse proxies. YARP wins on:

- **It is a library, not a framework.** YARP is middleware in an ordinary ASP.NET Core application, so a BFF
  is a normal app that happens to proxy. Adding a hand-written aggregation endpoint next to proxied routes
  is just adding a Minimal API endpoint — no plugin model, no escape hatch. Since aggregation is half the
  job, this matters more than routing features.
- **Actively developed by Microsoft**, used in production at scale internally. Ocelot is community-maintained
  and quieter.
- **Direct integration** with `HttpClientFactory`, Polly resilience handlers, health checks, and
  OpenTelemetry — the same building blocks every other service uses, rather than a parallel configuration
  system.
- **Configuration or code.** Routes come from `appsettings.json` for the simple cases and from code when
  dynamic.

Ocelot's advantage is a more batteries-included config file for pure routing. Since these are aggregating
BFFs rather than pure proxies, that advantage does not apply.

### What lives in a BFF, and what does not

**In:** routing, response aggregation, client-shaped DTOs, JWT validation (first line), CORS, rate limiting,
request correlation, and caching.

**Out:** business rules and domain logic. The moment a BFF decides *whether an order may be cancelled*
rather than *which calls compose the cancel-order screen*, the domain has leaked into the edge and the
owning service has been demoted to a data-access layer. The line is: **a BFF may decide what to call and how
to shape the answer; it may never decide what is true.**

## Consequences

### What this buys us
- Each client family evolves at its own pace without coordinating releases.
- Fewer round trips per screen — the product page is one call instead of three.
- Client-shaped payloads: mobile is not forced to over-fetch a desktop-sized response.
- Independent scaling and independent failure domains: a storefront traffic spike cannot exhaust the admin
  path.
- Sharply separated threat models — the public edge and the privileged edge are different processes.
- Services keep clean, general-purpose APIs instead of accreting client-specific endpoints.

### What this costs us
- **Three deployables instead of one**, with three sets of configuration, health checks, and dashboards.
- **Duplication between BFFs.** Storefront and mobile aggregate overlapping data. Some is genuinely shared
  and moves to a library; some is *deliberately* duplicated, because premature deduplication across BFFs
  recreates the single-gateway coupling this decision exists to avoid. Knowing which is which is a
  judgement call made per case, and getting it wrong in the "share it" direction is the more expensive
  error.
- **A new place for logic to hide.** Aggregation code is real code, and it is tempting to let a rule sneak
  in because it is convenient. Guarded by the rule above and by code review.
- **Double token validation** at BFF and service costs a little CPU. Accepted as defence in depth
  ([ADR-0005](0005-keycloak-as-identity-provider.md)); JWKS is cached, so there is no per-request network
  call.

### What we will have to revisit
If a fourth and fifth client appear (partner API, in-store kiosk), BFF-per-client stops scaling and the
answer becomes either a shared aggregation layer beneath thinner BFFs, or GraphQL federation as in Option C.
The trigger is aggregation logic being copied a third time.

## References

- [.NET microservices guide — API gateway pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/direct-client-to-microservice-communication-versus-the-api-gateway-pattern)
- [Sam Newman — Backends For Frontends](https://samnewman.io/patterns/architectural/bff/)
- [YARP documentation](https://microsoft.github.io/reverse-proxy/)
- [ADR-0014](0014-react-and-angular-in-lockstep.md) — why the two storefronts share one BFF
