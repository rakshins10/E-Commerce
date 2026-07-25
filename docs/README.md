# Documentation index

Everything about this system is reachable from here in two clicks. Documentation is written in the same pull
request as the code it describes — if you find a page that disagrees with the code, that is a bug, not a
stale draft.

**New here?** Read in this order: [Getting started](getting-started.md) → [Architecture](architecture.md) →
[Bounded contexts](domain/bounded-contexts.md) → [Concept map](concept-map.md).

---

## Start here

| Page | What it gives you |
|------|-------------------|
| [getting-started.md](getting-started.md) | A clean machine to a running system: tooling and versions, every port, every environment variable, the first-run sequence, how to verify each container is healthy, and a troubleshooting section for the failures that actually happen. |
| [architecture.md](architecture.md) | The C4 model — system context, containers, and the anatomy of a service. Communication styles and when each is used. The full service catalogue. |
| [concept-map.md](concept-map.md) | The interview cheat sheet. Every pattern in the system: what it is, why it is here, where it lives, and the interview question it answers. |

## The domain

Read these before the code. They explain the business, not the mechanics.

| Page | What it gives you |
|------|-------------------|
| [domain/glossary.md](domain/glossary.md) | The ubiquitous language. Every domain term defined precisely, including terms that mean different things in different contexts. |
| [domain/bounded-contexts.md](domain/bounded-contexts.md) | Every context: what it owns, what it deliberately does not own, why the boundary sits exactly there, and how it relates to its neighbours. |
| [domain/business-rules.md](domain/business-rules.md) | The invariants in plain language, before any code enforces them. |
| [domain/processes/](domain/processes/) | End-to-end process narratives — what actually happens when a customer places an order — told as a story, then as a sequence diagram, then as code references. |

## The services

One page per backend service: purpose, domain model, schema and migrations, dependencies, events in and out,
configuration, and a **complete endpoint reference** with auth, validation, error contract, idempotency, and a
`curl` example for every endpoint.

| Service | Page |
|---------|------|
| Catalog | [services/catalog.md](services/catalog.md) |
| Basket | [services/basket.md](services/basket.md) |
| Ordering | [services/ordering.md](services/ordering.md) |
| Payment | [services/payment.md](services/payment.md) |
| Inventory | [services/inventory.md](services/inventory.md) |
| Notification | [services/notification.md](services/notification.md) |
| User Profile | [services/user-profile.md](services/user-profile.md) |
| Back-office | [services/back-office.md](services/back-office.md) |
| Ordering Saga | [services/ordering-saga.md](services/ordering-saga.md) |
| Storefront BFF | [services/storefront-bff.md](services/storefront-bff.md) |
| Admin BFF | [services/admin-bff.md](services/admin-bff.md) |
| Mobile BFF | [services/mobile-bff.md](services/mobile-bff.md) |

## Integration

| Page | What it gives you |
|------|-------------------|
| [events/event-catalogue.md](events/event-catalogue.md) | Every integration event: name, version, publisher, subscribers, payload schema, ordering and idempotency expectations, and what happens on failure. |
| [diagrams/event-flow.md](diagrams/event-flow.md) | Publishers, subscribers, and the outbox path, drawn. |

## Security

| Page | What it gives you |
|------|-------------------|
| [authorization-model.md](authorization-model.md) | The full matrix: every realm role, every permission, which composite grants what, and which endpoint and UI action each one guards. |
| [diagrams/sequence-auth.md](diagrams/sequence-auth.md) | Login, token refresh, and service-to-service token acquisition as sequence diagrams. |

## The frontends

| Page | What it gives you |
|------|-------------------|
| [frontend/README.md](frontend/README.md) | How the four web apps and the mobile app are structured, and what the shared layer provides. |
| [frontend/shared-layer.md](frontend/shared-layer.md) | Design tokens, the generated API client, the OIDC flow, and the permission helpers — documented once, consumed everywhere. |
| [frontend/storefront/](frontend/storefront/) | Screen-by-screen catalogue for the storefront. |
| [frontend/admin/](frontend/admin/) | Screen-by-screen catalogue for the admin panel. |
| [react-vs-angular.md](react-vs-angular.md) | Per-feature comparison of the two implementations: where each framework was cleaner and what each forced us to work around. Written as the features were built. |

## Diagrams

All Mermaid, committed as text so they diff and review like code.

| Diagram | Page |
|---------|------|
| C4 — system context | [diagrams/c4-context.md](diagrams/c4-context.md) |
| C4 — containers | [diagrams/c4-container.md](diagrams/c4-container.md) |
| C4 — components (per service) | [diagrams/c4-component.md](diagrams/c4-component.md) |
| Bounded-context map | [diagrams/context-map.md](diagrams/context-map.md) |
| Deployment topology | [diagrams/deployment.md](diagrams/deployment.md) |
| Order state machine | [diagrams/order-state-machine.md](diagrams/order-state-machine.md) |
| Ordering aggregate boundary | [diagrams/ordering-aggregate.md](diagrams/ordering-aggregate.md) |
| Event flow and outbox | [diagrams/event-flow.md](diagrams/event-flow.md) |
| ERD per service | [diagrams/erd/](diagrams/erd/) |
| Sequence — auth flows | [diagrams/sequence-auth.md](diagrams/sequence-auth.md) |
| Sequence — checkout and saga | [diagrams/sequence-checkout.md](diagrams/sequence-checkout.md) |
| Sequence — admin actions | [diagrams/sequence-admin.md](diagrams/sequence-admin.md) |
| Frontend route maps | [diagrams/frontend-routes.md](diagrams/frontend-routes.md) |

## Decisions

[adr/](adr/) — Architecture Decision Records. Each one states the context, the options considered, the
decision, and the consequences accepted. **ADRs are immutable once merged**: a later reversal is a new ADR
that supersedes the old one, never an edit.

See [adr/README.md](adr/README.md) for the full index.

## Operations

| Page | What it gives you |
|------|-------------------|
| [operations/health-checks.md](operations/health-checks.md) | Every health check and what it actually proves — and the liveness/readiness distinction that matters when Kubernetes is layered on. |
| [operations/observability.md](operations/observability.md) | Structured logging, correlation IDs, and distributed tracing, with a worked example of tracing one order end to end. |
| [operations/runbook.md](operations/runbook.md) | Common operational tasks: reseeding, replaying a poisoned message, rotating a client secret, draining a queue. |

---

## Conventions used throughout

- Every page states **what, why, how, and the alternatives rejected** — not just what the code does.
- Code files implementing a non-obvious pattern carry a header comment naming the pattern and linking here.
- Cross-links are relentless: code → docs, docs → code paths, docs → the relevant chapter of the
  [Microsoft .NET microservices guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/).
- Anything simplified or mocked is labelled **Simplified for this repo** with a note on what production adds.
