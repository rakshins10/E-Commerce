# ADR-0012: CQRS with MediatR pipeline behaviours; Dapper on the read side

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1 (implemented in Phase 6)

## Context

Ordering has a rich domain model: an `Order` aggregate enforcing invariants, value objects, a state machine,
and domain events ([bounded-contexts.md — Ordering](../domain/bounded-contexts.md#ordering)). That model
exists to make **writes** correct.

Reads have opposite requirements. "Show this customer's last twenty orders with item counts and totals" needs
no invariants, no state machine, and no behaviour — it needs a fast projection of a few columns. Loading
twenty full `Order` aggregates with their items to render a summary table means hydrating hundreds of
entities, change-tracking every one, and discarding all of it.

Forcing both through one model degrades both: the domain model accumulates read-shaped properties and
aggregate-spanning navigation it does not need for its invariants, and reads get slow.

There is a second problem. Every command needs the same cross-cutting work — validate the input, log the
operation with a correlation id, open a transaction, dispatch domain events, deduplicate if it arrived from
the bus. Putting that in every handler is duplication that will drift.

## Options considered

### Option A — One model, repositories used for both reads and writes
Simplest. Fine for CRUD. Here it means the failure mode above: the aggregate becomes a read model in
disguise, and list endpoints load and discard graphs of entities.

### Option B — CQRS: separate command and query paths, one database
Commands go through the domain model and EF Core. Queries bypass the domain entirely and project straight to
DTOs. **Same database, same tables** — the separation is in the code path, not the storage.

### Option C — CQRS with separate read and write *stores*, kept in sync by events
Option B plus a physically separate, denormalised read store.

This is what Catalog does ([ADR-0003](0003-postgresql-and-polyglot-persistence.md)) because its read volume
justifies it. Ordering does not: its reads are user-scoped and modest, and a second store would add
projection lag and a whole consistency problem for no benefit. **Applying it everywhere is the most common
CQRS mistake** — CQRS does not require two databases, and saying so is usually the differentiator in an
interview.

### Option D — Full event sourcing
Store events as the source of truth and rebuild state by replay. Genuinely powerful — perfect audit, temporal
queries — and genuinely expensive: snapshotting, event versioning, and a mental model that touches
everything. Rejected as disproportionate to the domain. Named because "CQRS" and "event sourcing" are
routinely conflated, and separating them is a good answer.

## Decision

**CQRS in Ordering, over a single database (Option B), mediated by MediatR.**

| | Command side | Query side |
|---|---|---|
| Goes through | `Order` aggregate → `IOrderRepository` → EF Core | Dapper, direct SQL |
| Returns | nothing, or an identifier | flat DTOs shaped for the screen |
| Enforces | every invariant | nothing — the data is already valid |
| Tracked | yes (EF change tracking) | no |
| Transaction | yes, via a pipeline behaviour | none needed |

### Why Dapper on the read side rather than `AsNoTracking()`

`AsNoTracking()` removes change tracking but keeps the mapping to entity types, which keeps you in the
aggregate's shape — and encourages navigation properties added *for reads*, which quietly corrupt the write
model. Dapper is a deliberate hard boundary: read queries return purpose-built DTOs, written as SQL that
selects exactly the needed columns and joins. It is also faster and, importantly, the SQL is visible and
tunable rather than emergent from a translator.

**The rule: the query side may never touch a domain type.** If it does, the separation has failed.

### Why MediatR

Commands and queries become messages with handlers, and the endpoint's job shrinks to constructing a message
and sending it — the [mediator pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-application-layer-implementation-web-api),
decoupling the API layer from the application layer.

The real payoff is **pipeline behaviours** — cross-cutting concerns as composable middleware around every
handler, in order:

| Behaviour | Responsibility |
|-----------|----------------|
| `LoggingBehaviour` | Logs the request with correlation id and elapsed time |
| `ValidationBehaviour` | Runs FluentValidation validators; short-circuits with a `ProblemDetails` before the handler runs |
| `IdempotencyBehaviour` | Deduplicates commands originating from the bus ([ADR-0010](0010-transactional-outbox.md)) |
| `TransactionBehaviour` | Opens a transaction, calls `SaveChanges`, dispatches domain events, commits |
| `PerformanceBehaviour` | Warns on handlers exceeding a threshold |

Each is written once and applies to every handler. **Ordering matters and is a real design decision**:
validation must precede the transaction (do not open one for a request that will be rejected), and
idempotency must precede both (a duplicate should cost nothing).

`TransactionBehaviour` is where domain events are dispatched — after the aggregate's changes are in the
`DbContext` but inside the transaction — so that domain event handlers' writes and the aggregate's write
commit together, and integration events land in the outbox atomically.

### Domain events vs integration events

Consistently confused, so stated here:

| | **Domain event** | **Integration event** |
|---|---|---|
| Scope | inside one service, in-process | across services, over the bus |
| Timing | same transaction | after commit, via outbox |
| Naming | past tense, domain language (`OrderConfirmed`) | past tense, contract language (`OrderConfirmedIntegrationEvent`) |
| Coupling | may reference domain types | primitives only — a versioned published contract |
| If a handler throws | the whole transaction rolls back | the message is retried, then dead-lettered |

Domain events keep the aggregate from having to know about side effects. Integration events are how other
services find out. One is a modelling tool; the other is a transport contract.

## Consequences

### What this buys us
- Reads are fast and shaped for their screen; writes stay correct and expressive.
- The domain model never accumulates read-only concerns.
- Cross-cutting behaviour is written once and impossible to forget on a new handler.
- Handlers are trivially unit-testable — a message in, an assertion on the aggregate out, no HTTP, no DI
  container.
- Read and write sides can be optimised, and later scaled, independently.

### What this costs us
- **More files.** A command, its validator, its handler, and its DTO where a service method would have done.
  For genuinely CRUD services this is bureaucracy — which is why Basket does **not** do this. Complexity is
  spent only where the domain is complex.
- **Indirection.** `Send(command)` is not navigable to its handler by "go to definition". Real friction for
  newcomers, and the most legitimate criticism of MediatR.
- **Two query technologies** (EF Core and Dapper) means two sets of knowledge and two failure modes.
- **Hand-written SQL** on the read side is not refactoring-safe: renaming a column silently breaks a query
  until a test catches it. This is why the read side needs integration tests against a real database
  ([Testcontainers](../operations/runbook.md)).
- **Pipeline order is invisible coupling.** It is registration-order-dependent, easy to get wrong, and
  produces subtle bugs when wrong — so it is asserted in a test.

### What we will have to revisit
If Ordering's read volume ever justified it, Option C — a materialised read model fed by integration events —
is the next step, and the query-side interfaces are already shaped to allow it without touching the command
side. That is the point of drawing the boundary at the code path first.

## References

- [.NET microservices guide — CQRS and DDD patterns](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [Greg Young on CQRS](https://cqrs.files.wordpress.com/2010/11/cqrs_documents.pdf)
- [ADR-0003](0003-postgresql-and-polyglot-persistence.md) — where a separate read *store* was justified
- [ADR-0010](0010-transactional-outbox.md) — how integration events leave the transaction
