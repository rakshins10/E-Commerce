# ADR-0008: A single monorepo rather than a repository per service

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

Nine services, three BFFs, five building-block libraries, four web applications, one mobile app, a Keycloak
realm, compose files, and a documentation tree. These can live in one repository or in twenty.

Repository-per-service is often presented as the microservices-native layout, on the grounds that
independent deployability implies independent repositories. It does not — **independent deployability is a
property of the build and release pipeline, not of source-control topology.**

## Options considered

### Option A — Repository per service
Each service owns its history, its CI, its versioning, its access control. Strong ownership signal; nothing
can accidentally couple to another service's internals because it cannot see them.

Costs: a cross-cutting change (adding a claim to the token, changing an event's shape) spans many
repositories and many pull requests that must land in order. Shared building blocks must be published as
versioned NuGet packages before consumers can use them, so a one-line fix becomes publish-then-consume.
There is no single commit that represents "the system at this point in time", which makes reproducing a bug
across services genuinely hard. For a solo author, the overhead swamps the benefit.

### Option B — Monorepo
One repository, one history, one CI pipeline with path filters.

### Option C — Hybrid — a repo for backend, one for frontend
Splits along the biggest tooling seam (.NET vs Node).

Rejected because it cuts exactly across the changes that most need to be atomic. Adding a field to an API
touches a service, its OpenAPI document, the generated client, and four frontends. Splitting backend from
frontend puts that change in two repositories and guarantees a window where they disagree.

## Decision

**One monorepo**, laid out by concern:

```
/src        backend — services, gateways, building blocks
/web        the four web apps + shared, design-tokens, ui-spec
/mobile     React Native
/identity   Keycloak realm
/tests      unit, integration, contract, e2e
/deploy     compose
/docs       documentation
```

The decisive argument is **atomic cross-cutting change**. This repository's whole subject is the seams
*between* components: an integration event's contract, a permission that must be enforced in a service and
respected in four UIs, a design token consumed by React, Angular, and React Native. Every one of those is a
single logical change. In a monorepo it is one commit, one review, one CI run, and one revert. Split across
repositories it is a choreographed release with a window of inconsistency in the middle.

Two requirements make this near-decisive here:

- **[ADR-0014](0014-react-and-angular-in-lockstep.md)** requires React and Angular features to land in the
  *same pull request*. That is not expressible across repositories.
- **Documentation must ship with the code it describes** ([CONTRIBUTING.md](../../CONTRIBUTING.md)). Docs in
  a separate repository drift; that is the empirical norm, not a risk.

A monorepo is **not** a modular monolith. Services remain independently deployable — each has its own
Dockerfile, its own database, its own release path — and CI uses path filters so a Catalog change does not
rebuild Ordering. Sharing a repository is not sharing a process.

### Guarding against the real risk

The genuine danger is **accidental coupling**: with everything visible, adding a project reference from
Ordering to Catalog's internals is one keystroke, and nothing physically prevents it. Mitigations:

1. **Only `building-blocks/*` may be referenced across service boundaries**, and those contain infrastructure
   only — never domain types, never DTOs shared between two services' public APIs
   ([architecture.md §8](../architecture.md#8-cross-cutting-building-blocks)).
2. **A service project must never reference another service project.** Enforced by an architecture test in
   `/tests` that fails the build on violation — because a convention nobody checks is a convention nobody
   follows.
3. Cross-service contracts are `.proto` files and integration-event contract packages, both explicit and
   both reviewed.

## Consequences

### What this buys us
- Cross-cutting changes are one atomic, reviewable, revertible commit.
- One `git clone` and one `docker compose up` reproduces the entire system at any commit.
- Building blocks are consumed by project reference during development — no publish-then-consume cycle.
- Refactoring across boundaries is compiler-checked and IDE-assisted.
- Documentation cannot drift out of the repository it documents.
- The commit history reads as one coherent narrative, which is itself a deliverable here.

### What this costs us
- **Accidental coupling is possible** and must be actively policed. Addressed above, but it is real: the
  discipline is now social plus a test, rather than physical.
- **CI must be path-filtered** or every push rebuilds everything. Extra configuration, and it is easy to get
  the filters subtly wrong.
- **The repository is large** — a full clone pulls .NET, four Node applications, and a React Native app.
- **Coarse access control.** You cannot grant someone the Catalog service without granting everything. Fine
  for a solo repository; a genuine constraint at organisational scale.
- **Tooling heterogeneity in one tree.** `dotnet`, `npm`, and Expo coexist, and the CI configuration has to
  understand all three.

### What we will have to revisit
The trigger to split is organisational, not technical: multiple teams with genuinely independent release
cadences, or an access-control requirement that a monorepo cannot express. At that point the extraction is
mechanical (`git subtree split` preserves history) — but the building blocks must become published packages
first, and that is the work that makes the split expensive. Note that Google, Meta, and Microsoft run very
large monorepos, so scale alone is not the trigger.

## References

- [architecture.md §8](../architecture.md#8-cross-cutting-building-blocks) — what may be shared
- [ADR-0014](0014-react-and-angular-in-lockstep.md) — the requirement that most depends on this one
- [CONTRIBUTING.md](../../CONTRIBUTING.md) — docs-ship-with-code
