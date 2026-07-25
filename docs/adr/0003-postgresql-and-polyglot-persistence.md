# ADR-0003: PostgreSQL as the default engine, with polyglot persistence where the access pattern earns it

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

[ADR-0002](0002-microservices-over-modular-monolith.md) commits us to database-per-service. That raises two
questions: which engine is the default, and where do we deviate?

There is a practical constraint that turns out to be decisive. Nine services means nine database containers,
and the headline requirement is that `docker compose up` brings the entire system up on a developer laptop.
Whatever engine we pick is instantiated nine times, so its idle footprint is multiplied by nine.

There is also a pedagogical constraint: **polyglot persistence should appear where it is justified, and
nowhere else.** Using three databases to look sophisticated is the failure mode; using three databases
because three access patterns genuinely differ is the lesson.

## Options considered

### Option A — SQL Server everywhere
The default in most .NET shops and in Microsoft's own eShopOnContainers reference. Excellent EF Core
support, first-class tooling, and the engine most .NET interviewers will assume.

Disqualifying problem: **memory**. A SQL Server container reserves on the order of 1.5–2 GB at rest. Nine of
them is 15 GB before a single service starts. Even consolidating onto one instance with nine databases —
which is legitimate and is what a real deployment would do — costs ~2 GB and weakens the visual
demonstration that each service owns its own store. Licensing for anything beyond Developer edition is a
further wrinkle.

### Option B — PostgreSQL everywhere
Roughly 25–50 MB idle per container, so nine instances cost less than one SQL Server. Excellent EF Core
provider (Npgsql), and genuinely strong features for this domain: `jsonb` for semi-structured product
attributes, real full-text search, `SKIP LOCKED` for the outbox dispatcher and inventory reservations (which
is exactly the primitive a competing-consumers queue needs), and proper `NUMERIC` for money.

### Option C — One shared engine instance, one database per service
Physically shared, logically separated. This is what most real deployments do — a managed cluster with a
database per service. It preserves data sovereignty (the rule is that no service *reads another's tables*,
not that they run on different hardware) and is far cheaper.

Rejected only for the demonstration: a single container makes it too easy for a reader to assume shared
data. Separate containers make the boundary impossible to miss.

## Decision

**PostgreSQL 18 as the default engine, one container per service.** Deviate only where the access pattern
genuinely differs:

| Service | Store | Why this one |
|---------|-------|--------------|
| Ordering, Inventory, Payment, User Profile, Notification, Saga, Back-office | **PostgreSQL** | Relational data with real invariants, needing ACID within the aggregate. `SKIP LOCKED` for the outbox dispatcher and stock reservation. |
| Catalog — **write side** | **PostgreSQL** | Normalised model with foreign keys and constraints; correctness matters on write. |
| Catalog — **read side** | **MongoDB** | Read-dominated by orders of magnitude. Product pages are denormalised documents (product + variants + brand + category + images) served whole. Projecting to documents removes the joins from the hot path. This is [CQRS](0012-cqrs-with-mediatr.md) where it actually pays. |
| Basket | **Redis** | A cart is short-lived, disposable, key-value session state accessed by user id, with a natural TTL and no invariants worth enforcing in a schema. Durability is explicitly *not* wanted — an abandoned cart should expire. |

The deviations are each defensible in one sentence, which is the test. Notification does not get a document
store; Payment does not get an event store. **Where the access pattern is "relational data with
constraints", the answer is Postgres, and repeating that answer seven times is the right outcome.**

### On the demonstration-versus-production gap

Nine Postgres containers is a *pedagogical* choice, not what production would do. Production would use one
managed cluster (Azure Database for PostgreSQL, RDS) with a database and a dedicated login per service,
which preserves sovereignty at a fraction of the operational cost. This is stated in
[getting-started.md](../getting-started.md) so nobody copies the topology into a real deployment.

## Consequences

### What this buys us
- The whole stack fits comfortably on a laptop — around 400 MB for all nine databases.
- `SKIP LOCKED` gives a correct, contention-free outbox dispatcher and stock reservation without an external
  queue or advisory-lock gymnastics.
- `jsonb` handles variable per-category product attributes without an EAV table or a schema change per
  category.
- No licensing considerations at any scale.
- Three stores, each with a one-sentence justification — polyglot persistence demonstrated honestly.

### What this costs us
- **Departs from the .NET default.** Many interviewers will expect SQL Server, and eShopOnContainers uses
  it. Mitigated by the argument above being short and concrete; the reasoning is more interesting than the
  conformity would have been.
- **Npgsql quirks to know**: `timestamp with time zone` maps to `DateTime` with `Kind=Utc` and Npgsql is
  strict about it — a `DateTime.Now` (Local) reaching the database throws. This is arguably a feature, and
  the codebase uses `DateTimeOffset`/UTC throughout, but it surprises people once.
- **Two stores in Catalog means the read model can lag.** Projection is asynchronous, so a product edited in
  admin may take a moment to appear on the storefront. That is inherent to CQRS with a separate read store
  and is called out in the Catalog service page rather than hidden.
- **Redis is not durable by default.** A basket can be lost on restart. Accepted deliberately — see the
  table above — but it must be a conscious choice, not an accident.

### What we will have to revisit
If Catalog's read volume never justified a separate store, the Mongo read side should collapse into a
materialised view or an indexed projection table in Postgres — same CQRS shape, one fewer moving part. The
trigger to reconsider is the projection lag causing more support pain than the read performance saves.

## References

- [.NET microservices guide — database per microservice](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/data-sovereignty-per-microservice)
- [architecture.md §7 — data sovereignty](../architecture.md#7-data-sovereignty-why-services-never-share-a-database)
- [ADR-0012](0012-cqrs-with-mediatr.md) — the CQRS decision that the Mongo read side serves
- [ADR-0010](0010-transactional-outbox.md) — which depends on `SKIP LOCKED`
