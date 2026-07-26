# Architecture Decision Records

An ADR captures **one decision**: the context that forced it, the options genuinely considered, what was
chosen, and the consequences accepted. It is not documentation of how the code works — that lives in
[`docs/services/`](../services/) and [`docs/concept-map.md`](../concept-map.md). An ADR exists to answer
*"why is it like this?"* six months later, when the reasoning has evaporated and only the code remains.

**ADRs are immutable once merged.** If a later phase reverses a decision, write a new ADR that supersedes the
old one and mark the old one `Superseded by ADR-NNNN`. Never edit history — the fact that we once believed
something different is itself information.

Use [`0000-template.md`](0000-template.md) for new records.

---

## Index

| # | Decision | Status | Phase |
|---|----------|--------|-------|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions in this repo | Accepted | 1 |
| [0002](0002-microservices-over-modular-monolith.md) | Microservices over a modular monolith — and why that is the *wrong* production call here | Accepted | 1 |
| [0003](0003-postgresql-and-polyglot-persistence.md) | PostgreSQL as the default engine; Redis and MongoDB where the access pattern justifies them | Accepted | 1 |
| [0004](0004-identity-vs-profile-data-split.md) | Split identity data (Keycloak) from profile data (User Profile service) | Accepted | 1 |
| [0005](0005-keycloak-as-identity-provider.md) | Keycloak as the identity provider — vs Entra ID, Auth0, Duende, and rolling our own | Accepted | 1 |
| [0006](0006-yarp-gateway-and-bff-per-client.md) | YARP with a BFF per client family, not a single gateway | Accepted | 1 |
| [0007](0007-grpc-for-internal-sync-calls.md) | gRPC for internal synchronous calls, REST at the edge | Accepted | 1 |
| [0008](0008-monorepo.md) | A single monorepo rather than a repository per service | Accepted | 1 |
| [0009](0009-secrets-management.md) | Secrets stay out of source; dev fixtures are labelled and worthless | Accepted | 1 |
| [0010](0010-transactional-outbox.md) | Transactional Outbox for reliable event publishing | Accepted | 1 (built in 6) |
| [0011](0011-orchestration-saga.md) | Orchestration-based saga, not choreography | Accepted | 1 (built in 7) |
| [0012](0012-cqrs-with-mediatr.md) | CQRS with MediatR pipeline behaviours; Dapper on the read side | Accepted | 1 (built in 6) |
| [0013](0013-net10-target-framework.md) | Target .NET 10 (LTS) | Accepted | 1 |
| [0014](0014-react-and-angular-in-lockstep.md) | Build React and Angular simultaneously against a shared, framework-neutral layer | Accepted | 1 |
| [0015](0015-manual-mappers-over-automapper.md) | Hand-written mappers instead of AutoMapper | Accepted | 1 |
| [0016](0016-rabbitmq-behind-ieventbus.md) | RabbitMQ as the broker, behind an `IEventBus` abstraction | Accepted | 1 |
| [0017](0017-cloud-portable-architecture.md) | Cloud-portable: the same code runs on Docker or on Azure | Accepted | 3 |
| [0018](0018-self-contained-frontends.md) | Each frontend owns its code, even where that duplicates | Accepted | 3 |

Planned for later phases: permission-based policies over role checks (Phase 2), read-model projection
strategy for Catalog (Phase 4), resilience policy defaults (Phase 12).

---

## Status values

| Status | Meaning |
|--------|---------|
| **Proposed** | Under discussion; not yet acted on. |
| **Accepted** | In force. The code reflects it. |
| **Superseded by ADR-NNNN** | No longer in force. Kept for the historical record. |
| **Deprecated** | No longer relevant (the thing it decided about was removed). |
