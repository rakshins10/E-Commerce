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
| [operations/tooling-guide.md](operations/tooling-guide.md) | **Never used Seq, Jaeger, RabbitMQ or Keycloak?** Start here. A hands-on introduction to all four, assuming no prior knowledge, with exercises you can run against the live stack. |

## The domain

Read these before the code. They explain the business, not the mechanics.

| Page | What it gives you |
|------|-------------------|
| [domain/glossary.md](domain/glossary.md) | The ubiquitous language. Every domain term defined precisely, including terms that mean different things in different contexts. |
| [domain/bounded-contexts.md](domain/bounded-contexts.md) | Every context: what it owns, what it deliberately does not own, why the boundary sits exactly there, and how it relates to its neighbours. |
| [domain/business-rules.md](domain/business-rules.md) | The invariants in plain language, before any code enforces them — and the distinction between an invariant, a business rule, and a policy. |

## The services

[**services/README.md**](services/README.md) — one page per backend service: purpose, domain model, schema and
migrations, dependencies, events in and out, configuration, and a **complete endpoint reference** with auth,
validation, error contract, idempotency, and a `curl` example for every endpoint. Pages land with the service
they describe; the index lists which phase each arrives in.

## Integration

| Page | What it gives you |
|------|-------------------|
| [events/event-catalogue.md](events/event-catalogue.md) | Every integration event: name, version, publisher, subscribers, payload schema, ordering and idempotency expectations, and what happens on failure. |

## Security

| Page | What it gives you |
|------|-------------------|
| [authorization-model.md](authorization-model.md) | The full matrix: every realm role, every permission, which composite grants what, and which endpoint and UI action each one guards. |

## The frontends

| Page | What it gives you |
|------|-------------------|
| [frontend/README.md](frontend/README.md) | The screen catalogue: how the four web apps and the mobile app are structured, what the shared layer provides, and what every screen page covers. |
| [react-vs-angular.md](react-vs-angular.md) | Per-feature comparison of the two implementations: where each framework was cleaner and what each forced us to work around. Written as the features were built. |
| [`web/parity-checklist.md`](../web/parity-checklist.md) | Every screen and behaviour × React status × Angular status. |

## Diagrams

[**diagrams/README.md**](diagrams/README.md) — the index, with drawing conventions and which phase each
diagram arrives in. All Mermaid, committed as text so they diff and review like code.

Available now: [C4 context, container and component](architecture.md#2-c4-level-1--system-context) ·
[bounded-context map](domain/bounded-contexts.md#the-context-map) ·
[deployment topology](diagrams/deployment.md) ·
[saga flow with compensation](adr/0011-orchestration-saga.md#the-flow).

## Decisions

[adr/](adr/) — Architecture Decision Records. Each one states the context, the options considered, the
decision, and the consequences accepted. **ADRs are immutable once merged**: a later reversal is a new ADR
that supersedes the old one, never an edit.

See [adr/README.md](adr/README.md) for the full index.

## Operations

| Page | What it gives you |
|------|-------------------|
| [operations/tooling-guide.md](operations/tooling-guide.md) | Hands-on introduction to Seq, Jaeger, RabbitMQ and Keycloak for someone who has not used them before. |
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
