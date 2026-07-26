# ADR-0002: Microservices over a modular monolith

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

The system is an online store with nine bounded contexts. It has one developer, no traffic, no uptime
obligation, and no team boundaries to respect. On the merits of the *domain alone*, this is a textbook case
for a modular monolith.

But the domain is not the only requirement. The stated purpose of the repository is to demonstrate — and let
the author defend in an interview — every architectural concept in Microsoft's
[.NET microservices guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/):
service boundaries, database-per-service, integration events, the transactional outbox, sagas with
compensation, API gateways and BFFs, distributed tracing, resilience policies. **Most of those patterns
exist to solve problems that only appear once the process boundary exists.** You cannot honestly demonstrate
a saga in a system where a database transaction would do.

So the decision is not really "which architecture is best for a store". It is: *given that the goal is to
demonstrate distributed patterns correctly, do we adopt the architecture those patterns belong to, or
simulate it?*

## Options considered

### Option A — Modular monolith
One deployable, one database with a schema per module, modules as separate assemblies with enforced
dependency rules (ArchUnitNET or Roslyn analysers), communicating in-process through an internal mediator.

Genuinely strong: ACID transactions across contexts, refactoring boundaries is a compiler-checked rename,
one thing to deploy and debug, no network failure modes, sub-millisecond calls, trivially fast local
startup. **This is the correct production choice for a store at this scale**, and the industry has spent the
last few years correcting itself back toward it.

Its weakness for our purpose: an outbox, a saga, idempotent consumers, and a circuit breaker would all be
theatre. There is no partial failure to survive, so the code demonstrating survival is unfalsifiable.

### Option B — Microservices
Nine independently deployable services, database per service, integration events over a broker, sagas for
cross-service consistency.

Strong where the modular monolith is weak: independent deployability and scaling, fault isolation,
technology heterogeneity where the access pattern justifies it (Redis for the basket, Mongo for the catalog
read side). And, decisively here: every pattern in the guide has a real reason to exist.

Costs are real and are paid every single day — see Consequences.

### Option C — Monolith with a couple of services extracted
The pragmatic middle: keep everything in-process except the two or three contexts with genuinely different
profiles.

This is what a good team actually does, and it is the migration path recommended below. It was rejected only
because a partial demonstration would leave half the guide's patterns unexercised — the point of the
artefact is coverage.

## Decision

**Build microservices, and state plainly and repeatedly in the documentation that a modular monolith would
be the better production choice at this scale.**

The honesty is not a disclaimer; it is part of the deliverable. An engineer who can only argue *for*
microservices has not understood them. The strongest version of this answer in an interview is: *"Here is a
correct microservices implementation, and here is why I would not have built it this way for this business."*

The architecture doc opens with this argument rather than burying it —
[architecture.md §1](../architecture.md#1-the-honest-framing-should-this-be-microservices-at-all).

### The migration path a real team would take

Stated because "how would you decide when to extract a service?" is the follow-up question:

1. **Start as a modular monolith** with strictly enforced module boundaries — no module referencing another's
   internals, no cross-schema queries. Boundaries enforced by analysers, not by good intentions.
2. **Communicate in-process through an abstraction** (an internal mediator/event dispatcher) that mirrors the
   shape of an out-of-process one. This is the key move: it makes later extraction a change of transport
   rather than a rewrite.
3. **Extract when a seam actually hurts**, and only then. Legitimate triggers: a module needs to scale
   independently; a module's failure keeps taking down unrelated functionality; a separate team needs to
   deploy on its own cadence; a module has materially different compliance or data-residency requirements.
4. **Extract the leaf first** — a module others do not depend on, typically a generic subdomain like
   notifications. Lowest risk, and it forces the team to build the operational muscle (deployment pipeline,
   tracing, broker) before anything critical depends on it.

Note that "our codebase is getting big" is **not** on that list. Size is a modularity problem; distribution
is not its cure.

## Consequences

### What this buys us
- Independent deployability and independent scaling per service.
- Fault isolation: Ordering can be down while Catalog serves browse traffic.
- Polyglot persistence where the access pattern genuinely differs (see [ADR-0003](0003-postgresql-and-polyglot-persistence.md)).
- A setting in which outbox, saga, idempotency, circuit breakers, and distributed tracing are *load-bearing*
  rather than decorative — which is the entire point of the repository.

### What this costs us
- **No cross-service transactions.** Consistency between Ordering, Inventory, and Payment is eventual and
  maintained by a saga with compensating actions. This is strictly harder to reason about and to test than
  `BEGIN TRANSACTION`, and it is the single largest tax in the system.
- **Partial failure is now a design concern in every call.** Retries, timeouts, circuit breakers, and
  idempotent consumers are mandatory, not optional polish.
- **Debugging requires infrastructure.** A single order touches five services; without distributed tracing
  and correlation IDs the system is effectively undebuggable. That is why observability is not deferred.
- **Local development is heavy.** ~30 containers and several GB of RAM to run what one process could.
- **Refactoring a boundary is expensive.** Moving a responsibility between services means a data migration
  and a coordinated deploy, where the monolith needed a rename. Boundaries must therefore be right early —
  which is precisely why [bounded-contexts.md](../domain/bounded-contexts.md) was written before the code.
- **Operational surface**: nine services, nine databases, a broker, and an IdP all need health checks,
  configuration, and lifecycle management.

### What we will have to revisit
Nothing, within this repository's purpose. In a real product, the trigger to reverse this decision would be
the absence of the extraction triggers listed above — if no seam ever hurts, the distribution was never
earned, and consolidating back is legitimate engineering rather than an admission of failure.

## References

- [.NET microservices guide — architecting container and microservice-based applications](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/)
- [architecture.md §1](../architecture.md#1-the-honest-framing-should-this-be-microservices-at-all)
- [domain/bounded-contexts.md](../domain/bounded-contexts.md) — how the boundaries were drawn
- [ADR-0011](0011-orchestration-saga.md) — the consistency mechanism this decision makes necessary
