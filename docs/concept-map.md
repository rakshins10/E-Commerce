# Concept map

> **Related:** [Architecture](architecture.md) · [ADR index](adr/README.md) ·
> [Bounded contexts](domain/bounded-contexts.md)

The interview cheat sheet. For every pattern in this system: **what it is**, **why it is here**, **where it
lives**, and **the interview question it answers**.

The `Phase` column says when each lands. Entries marked with a phase later than the current one are
documented — the decision has been made and argued in an ADR — but not yet coded. That is deliberate: the
reasoning is the harder half, and writing it first gives the code something to conform to.

---

## Microservice fundamentals

### Bounded context

**What:** the boundary within which one model and one vocabulary are consistent. A *linguistic* boundary
before a technical one — if the same word means two different things to two groups, you have found an edge.

**Why here:** it is what decides the service boundaries. `Customer` means three different things in this
system — a credential set to Keycloak, a profile to User Profile, a frozen `BuyerId` and address to Ordering
— which is precisely why there is no canonical `Customer` class.

**Where:** [`docs/domain/bounded-contexts.md`](domain/bounded-contexts.md) · Phase 1

**Interview question:** *"How do you decide where one microservice ends and the next begins?"*

### Database per service / data sovereignty

**What:** every service owns its data exclusively. No shared tables, no cross-service foreign keys, no
cross-service joins.

**Why here:** it is the rule that separates a microservice architecture from a **distributed monolith**. If
Ordering read Catalog's tables, Catalog could not change its schema without a coordinated release — you would
have paid the whole distributed-systems tax while keeping every constraint of a monolith.

**Where:** nine separate database containers in [`deploy/docker-compose.yml`](../deploy/docker-compose.yml) ·
[architecture.md §7](architecture.md#7-data-sovereignty-why-services-never-share-a-database) ·
[ADR-0003](adr/0003-postgresql-and-polyglot-persistence.md) · Phase 1

**Interview question:** *"Two services need the same data. What do you do?"* — and the follow-up,
*"How do you handle a foreign key across services?"* (You do not. Validate at write time, snapshot what you
need.)

### Polyglot persistence

**What:** choosing the datastore per service according to its access pattern rather than by convention.

**Why here:** Redis for the basket (short-lived, disposable, key-value, TTL), MongoDB for the Catalog read
side (denormalised documents, read-dominated), Postgres everywhere else (relational data with real
invariants). Each deviation is defensible in one sentence — that is the test. Using three databases to look
sophisticated is the failure mode.

**Where:** [ADR-0003](adr/0003-postgresql-and-polyglot-persistence.md) · Phase 1

**Interview question:** *"When would you use something other than a relational database?"*

### Monorepo without a distributed monolith

**What:** one repository, many independently deployable services.

**Why here:** this system's subject is the seams *between* components — an event contract, a permission
enforced in a service and respected in four UIs. Each is one logical change, and a monorepo makes it one
commit, one review, one revert. Independent deployability is a property of the build pipeline, not of
source-control topology.

**Where:** [ADR-0008](adr/0008-monorepo.md) · enforced by
[`tests/unit/ECommerce.Architecture.Tests`](../tests/unit/ECommerce.Architecture.Tests/) · Phase 1

**Interview question:** *"Monorepo or repo per service?"*

---

## Communication

### API Gateway / Backends for Frontends

**What:** an edge component per *client experience* that routes and aggregates.

**Why here:** a single gateway for five clients becomes a coupling magnet — every client's needs in one
codebase, aggregation logic full of `if (client == "mobile")`, and one team's cadence gating everyone's.
Three BFFs instead. Note the asymmetry: the React and Angular storefronts **share** one BFF, because a BFF
exists per client experience, not per framework — which is also what makes their parity provable.

**Where:** [`src/gateways/`](../src/gateways/) · [ADR-0006](adr/0006-yarp-gateway-and-bff-per-client.md) ·
Phase 3

**Interview question:** *"Why not have the browser call the services directly?"* and *"Why more than one
gateway?"*

### Synchronous vs asynchronous, and gRPC vs REST

**What:** events by default; synchronous calls only when the caller is blocked on the answer. gRPC between
services, REST at the edge.

**Why here:** a synchronous call makes A's availability the *product* of A's and B's. The decision rule is
written down rather than made case by case, and the resulting call inventory is deliberately tiny — four
sites. gRPC's `.proto` is a compiler-enforced contract, which is what you want internally and precisely what
you do not want at a browser boundary.

**Where:** [architecture.md §6](architecture.md#6-communication-choosing-synchronous-or-asynchronous) ·
[ADR-0007](adr/0007-grpc-for-internal-sync-calls.md) · Phase 4

**Interview question:** *"REST or gRPC?"* — the good answer is "for which hop?"

### The snapshot rule

**What:** Ordering copies product name and price into `OrderItem` at checkout rather than referencing them.

**Why here:** primarily **domain correctness** — an order records what the customer agreed to pay, and a price
change next week must not retroactively alter last week's order. It also severs the runtime dependency, which
is the general shape of a well-drawn boundary: the right domain answer and the right coupling answer agree.

**Where:** [architecture.md §6](architecture.md#the-snapshot-rule) · Phase 6

**Interview question:** *"Isn't copying that data denormalisation?"*

---

## Event-driven architecture and consistency

### Integration events and publish/subscribe

**What:** a service announces a fact that has already happened; interested services react. Past tense, always
— a subscriber may react, never veto.

**Why here:** it is the primary integration mechanism, and it is what lets Notification be switched off
entirely without anything upstream noticing.

**Where:** [`src/building-blocks/EventBus/`](../src/building-blocks/EventBus/) ·
[event catalogue](events/event-catalogue.md) · Phase 1 (abstraction), Phase 6 (first real events)

**Interview question:** *"Event or command — how do you tell?"*

### Transactional Outbox

**What:** the event is written to a table in the **same transaction** as the business data; a separate
dispatcher publishes it and marks the row processed.

**Why here:** it solves the **dual-write problem**. Saving the order then publishing means a crash between the
two leaves an order nothing is fulfilling — silent, permanent, and it will not show up in testing. The outbox
reduces two transactional resources to one: either both commit or neither does.

**Where:** [ADR-0010](adr/0010-transactional-outbox.md) · Phase 6

**Interview question:** *"How do you guarantee an event is published when the transaction commits?"* — and the
follow-up on why 2PC is not the answer.

### At-least-once delivery and the idempotent consumer

**What:** delivery may duplicate; consumers record processed message ids in their own database, inside the
same transaction as the business work, and ignore repeats.

**Why here:** **exactly-once delivery is not achievable** across a network. What is achievable is
effectively-once *processing*. The outbox and the idempotent consumer are two halves of one pattern:
implementing only the outbox moves the bug from the publisher to the consumer, where it manifests as a
customer charged twice.

**Where:** [`IIntegrationEventHandler`](../src/building-blocks/EventBus/IIntegrationEventHandler.cs) ·
Phase 1 (contract), Phase 6 (implementations)

**Interview question:** *"Can you guarantee exactly-once delivery?"* — the correct answer is no, and then
what you do instead.

### Saga with compensating transactions

**What:** a sequence of local transactions, each with a compensating action that *semantically* undoes it.

**Why here:** there is no distributed transaction, so a failed payment after a stock reservation must release
that stock or the inventory is leaked. Compensation is a **business** operation, not a rollback — you cannot
un-send an email; you send a correction.

**Where:** [`src/services/ordering-saga/`](../src/services/ordering-saga/) ·
[ADR-0011](adr/0011-orchestration-saga.md) · Phase 7

**Interview question:** *"How do you keep data consistent across services without a distributed transaction?"*

### Orchestration vs choreography

**What:** an explicit process manager drives the steps, rather than each service reacting to the previous
one's event.

**Why here:** with choreography the process exists *nowhere* — it is emergent, and compensation logic smears
across every participant until Inventory knows about payment. Orchestration concentrates that coupling
somewhere you can read, test, and query. The cost — a single point of coordination — is mitigated by the
orchestrator communicating only asynchronously.

**Where:** [ADR-0011](adr/0011-orchestration-saga.md) · Phase 7

**Interview question:** *"Choreography or orchestration, and why?"*

### Eventual consistency

**What:** parts of the system are briefly out of step and converge.

**Why here:** the interesting part is knowing where it is *not* acceptable. Product-page stock may be seconds
stale — fine, because the authoritative check happens at reservation. The reservation itself may not be
stale. Same domain, both halves, and the distinction is the whole skill.

**Where:** [bounded-contexts.md — Catalog](domain/bounded-contexts.md#catalog) · Phase 6

**Interview question:** *"When is eventual consistency acceptable?"*

---

## Domain-Driven Design

### Entity vs value object

**What:** an entity is defined by identity; a value object by its attributes, is immutable, and compares
structurally.

**Why here:** the test — *if I change every attribute, is it still the same thing?* An entity must therefore
**not** be a C# `record`: records give value equality, which is exactly wrong for an entity and breaks subtly
in sets and dictionaries. Value objects also let the type system carry meaning: `Money + Money` can refuse to
add different currencies where `decimal + decimal` cannot.

**Where:** [`Entity.cs`](../src/building-blocks/Common/SeedWork/Entity.cs) ·
[`ValueObject.cs`](../src/building-blocks/Common/SeedWork/ValueObject.cs) · Phase 1

**Interview question:** *"Entity or value object — how do you decide?"*

### Aggregate and aggregate root

**What:** a cluster of objects treated as one unit, and — the part that matters — **one consistency
boundary**.

**Why here:** `Order` + `OrderItem`s must never disagree, so they are one aggregate. The buyer is *outside*,
referenced by id, because an order does not need the customer record consistent with it at every instant.
Draw the boundary at what must be transactionally consistent, not at what is convenient to load together;
when in doubt, prefer smaller.

**Where:** [`IAggregateRoot.cs`](../src/building-blocks/Common/SeedWork/IAggregateRoot.cs) ·
[aggregate diagram](diagrams/README.md) · Phase 1 (seedwork), Phase 6 (the `Order` aggregate)

**Interview question:** *"How big should an aggregate be?"*

### Invariants enforced inside the aggregate

**What:** rules that must always hold, enforced by the aggregate itself rather than by callers.

**Why here:** if callers enforce them, every new caller is a chance to forget. Guard clauses in constructors
plus immutability mean *if you are holding one, it is valid* — "parse, don't validate" applied to domain types.

**Where:** [`Guard.cs`](../src/building-blocks/Common/Guards/Guard.cs) ·
[`DomainException.cs`](../src/building-blocks/Common/Exceptions/DomainException.cs) · Phase 1

**Interview question:** *"Where does validation belong?"*

### Domain events vs integration events

**What:** domain events are in-process and inside the transaction; integration events cross services after
commit, via the outbox.

**Why here:** routinely conflated, and the difference has real consequences — if a domain event handler
throws, the whole transaction rolls back; if an integration event handler throws, the message is retried and
then dead-lettered. Note that this codebase's `IDomainEvent` deliberately references **nothing**, unlike
eShopOnContainers which couples its `Entity` to MediatR's `INotification`, so the domain project's dependency
list is genuinely empty.

**Where:** [`IDomainEvent.cs`](../src/building-blocks/Common/SeedWork/IDomainEvent.cs) ·
[ADR-0012](adr/0012-cqrs-with-mediatr.md) · Phase 1

**Interview question:** *"What is the difference between a domain event and an integration event?"*

### Repository and Unit of Work

**What:** one repository per aggregate root; the EF Core `DbContext` **is** the Unit of Work.

**Why here:** the interface is declared in the domain (a domain concept) and implemented in infrastructure —
that inversion is what "dependencies point inward" means concretely. Note what is missing: no `SaveChanges`
on the repository, and no hand-written `IUnitOfWork` wrapper, which is a very common redundant abstraction
over an abstraction. Also no `IQueryable`, which would leak the persistence model to callers.

**Where:** [`IRepository.cs`](../src/building-blocks/Common/SeedWork/IRepository.cs) · Phase 1

**Interview question:** *"Do you need a Unit of Work with EF Core?"*

---

## CQRS

### Separate command and query models

**What:** commands go through the domain model; queries bypass it entirely and project straight to DTOs.

**Why here:** loading twenty `Order` aggregates to render a summary table hydrates hundreds of entities and
discards them. Dapper on the read side is a *hard* boundary — `AsNoTracking()` would keep you in the
aggregate's shape and encourage navigation properties added for reads, which corrupts the write model.
**CQRS does not require two databases**, and saying so is usually the differentiator.

**Where:** [ADR-0012](adr/0012-cqrs-with-mediatr.md) · Phase 6

**Interview question:** *"Does CQRS mean two databases?"*

### Mediator and pipeline behaviours

**What:** commands and queries are messages; cross-cutting concerns are composable middleware around every
handler.

**Why here:** validation, logging, idempotency, and transactions written once instead of per handler.
**Order is a real design decision**: validation before the transaction (do not open one for a request that
will be rejected), idempotency before both (a duplicate should cost nothing).

**Where:** [ADR-0012](adr/0012-cqrs-with-mediatr.md) · Phase 6

**Interview question:** *"What does MediatR actually buy you?"* — and the fair criticism, that `Send()` is not
navigable to its handler.

---

## Resilience

### Retry with exponential backoff and jitter

**What:** re-attempt a transient failure with growing, randomised delays.

**Why here:** jitter is the part people omit. Without it, every service that started together retries at
exactly the same instants — a synchronised thundering herd against a dependency that is already struggling.

**Where:** [`RabbitMqConnection.cs`](../src/building-blocks/EventBus.RabbitMQ/RabbitMqConnection.cs) ·
Phase 1, expanded in Phase 12

**Interview question:** *"What is wrong with a simple retry loop?"*

### Circuit breaker vs transient-fault handling

**What:** retry handles a *blip*; a circuit breaker handles a dependency that is *down*, by failing fast
instead of queueing doomed calls.

**Why here:** they solve opposite problems and are frequently conflated. Retrying against a dead dependency
makes things worse — every caller holds a connection and a thread waiting to fail, so the caller exhausts its
own resources and the outage spreads. The breaker's job is to stop that propagation and to give the
dependency room to recover.

**Where:** Phase 12

**Interview question:** *"Retry or circuit breaker?"* — the answer is both, for different failures.

---

## Observability

### Health checks: liveness vs readiness

**What:** liveness asks *is this process irrecoverably broken* (restart me); readiness asks *can I serve
traffic now* (stop routing to me).

**Why here:** conflating them is actively dangerous. Put a database check in liveness and a brief database
outage makes every replica fail liveness at once — the orchestrator restarts them all, restarting does not fix
a database, and you have converted a recoverable blip into an outage plus a crash loop. **Liveness must never
check anything a restart cannot fix.**

**Where:** [`HealthCheckExtensions.cs`](../src/building-blocks/Observability/HealthCheckExtensions.cs) ·
Phase 1

**Interview question:** *"What is the difference between a liveness and a readiness probe?"* — the best answer
names the crash-loop failure mode.

### Correlation IDs

**What:** one identifier following a request across every service and every asynchronous hop.

**Why here:** it is adopted from an inbound header rather than regenerated at each hop — regenerating produces
several disconnected fragments of one user action. It coexists with tracing rather than duplicating it: traces
are sampled, logs usually are not, and a correlation id is something a human can quote down the phone.

**Where:** [`CorrelationId.cs`](../src/building-blocks/Observability/CorrelationId.cs) · Phase 1

**Interview question:** *"You have a trace id already — why also a correlation id?"*

### Distributed tracing

**What:** one trace spanning every service involved in an operation.

**Why here:** with nine services, an order that fails somewhere is otherwise undebuggable. The hard part is
the **asynchronous hop**: automatic instrumentation cannot see across a broker, so the `traceparent` is
carried on the message and the span re-parented on the consumer side.

**Where:** [`ObservabilityExtensions.cs`](../src/building-blocks/Observability/ObservabilityExtensions.cs) ·
Phase 1, completed in Phase 12

**Interview question:** *"How do you trace a request across an async message boundary?"*

---

## Security

### External identity provider

**What:** authentication delegated to Keycloak.

**Why here:** authentication is a generic subdomain — every business needs it, none wins by doing it better,
and doing it slightly wrong is catastrophic. The strongest argument is not integration cost: it is that
"we wrote our own auth" is a finding in every security review.

**Where:** [ADR-0005](adr/0005-keycloak-as-identity-provider.md) · Phase 2

**Interview question:** *"Why not build your own login?"* — and the four-way comparison of Keycloak vs Entra
ID vs Auth0 vs Duende.

### Authorization Code + PKCE, and public clients

**What:** the browser and mobile flows use PKCE and hold **no** client secret.

**Why here:** a secret in a JavaScript bundle or an app binary is not a secret. PKCE replaces it — the client
proves possession of a verifier it generated, so an intercepted authorization code is useless. The mobile app
uses the *system browser*, not a webview, because a webview lets the host app observe credentials.

**Where:** [ADR-0005](adr/0005-keycloak-as-identity-provider.md) · Phase 2

**Interview question:** *"Why does a SPA not use a client secret?"*

### Defence in depth: validate at the gateway and at the service

**What:** both the BFF and the service validate the JWT independently — signature via JWKS, issuer, lifetime,
and **audience**.

**Why here:** the gateway is not a trust boundary worth betting everything on. Audience validation is the
check most often skipped, and skipping it means a token minted for another client is accepted.

**Where:** [`src/building-blocks/Auth/`](../src/building-blocks/Auth/) · Phase 2

**Interview question:** *"If the gateway already validated the token, why validate it again?"*

### Permission-based policies over role checks

**What:** endpoints declare the capability they need (`order:refund`); Keycloak composite roles decide which
roles hold it.

**Why here:** role checks encode *job titles* where the code means *capabilities*. When `support-agent` gains
refund rights, a role-based codebase needs every attribute found and edited — and the ones you miss fail
silently in the permissive direction.

**Where:** [`docs/authorization-model.md`](authorization-model.md) · Phase 2

**Interview question:** *"Roles or permissions?"*

### Resource-based authorization

**What:** a customer may read only *their own* orders — a decision that depends on the resource, not just the
claims.

**Why here:** it cannot be expressed as a policy over claims alone, which is exactly why ASP.NET Core has a
separate resource-based authorization API.

**Where:** Phase 2

**Interview question:** *"How do you stop a user reading someone else's order?"*

### The server is the only enforcement point

**What:** the UI hides actions the token does not permit; the server independently enforces the same rule.

**Why here:** hiding a button is **user experience**, not security — anyone can call the API directly with the
same token. There is a test for this: the authorization suite calls protected endpoints with a
lower-privileged token and asserts rejection.

**Where:** [`docs/authorization-model.md`](authorization-model.md) · Phase 2

**Interview question:** *"You hid the button. Is that enough?"*

---

## Delivery

### React and Angular in lockstep

**What:** every UI feature lands in both frameworks in the same pull request, proven by one Playwright suite
run against both.

**Why here:** building React first and porting later reliably fails — the port is never finished, and what is
finished is Angular a reviewer recognises as translated React. A checklist is a claim; a passing suite is
evidence.

**Where:** [ADR-0014](adr/0014-react-and-angular-in-lockstep.md) ·
[`web/parity-checklist.md`](../web/parity-checklist.md) · Phase 3 onward

**Interview question:** *"How do you keep two implementations of the same UI from drifting?"*

### Warnings as errors, and dependency auditing in the build

**What:** `TreatWarningsAsErrors` plus NuGet audit, with suppressions recorded next to their reason.

**Why here:** it is not pedantry — it caught a real supply-chain problem in this repository, when the
OpenTelemetry version originally pinned turned out to carry known advisories. A warning nobody reads is not a
control.

**Where:** [`Directory.Build.props`](../Directory.Build.props) · [`.editorconfig`](../.editorconfig) · Phase 1

**Interview question:** *"How do you stop a vulnerable transitive dependency shipping?"*
