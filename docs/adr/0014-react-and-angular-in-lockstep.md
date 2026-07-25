# ADR-0014: Build React and Angular simultaneously against a shared, framework-neutral layer

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

The requirement is four web applications — a storefront and an admin panel in **both** React and Angular —
that are *functionally and visually identical*. The purpose is to demonstrate fluency in both ecosystems.

The obvious plan is to build all of React first, then port to Angular. That plan reliably produces a poor
result, for reasons worth stating precisely:

- **The port is never finished.** The second implementation is perceived as duplicate work with no new
  functionality, so it is deprioritised the moment anything else competes for attention. Half-finished
  Angular is worse than no Angular — it demonstrates abandonment rather than competence.
- **A port copies architecture, not idiom.** Porting React hooks and `useEffect` to Angular yields Angular
  that a reviewer immediately recognises as translated: `BehaviorSubject` where a signal belongs, manual
  subscription management where `async` pipe belongs, services shaped like hooks. It demonstrates the
  *opposite* of the intended point.
- **Drift is invisible.** Without a shared specification, the two implementations diverge in small ways —
  a different validation message, a different empty state — and nothing detects it.
- **Shared logic gets written twice.** Auth handling, permission checks, and API calls end up duplicated
  and then diverge, which is the most expensive kind of duplication.

## Options considered

### Option A — React first, Angular later
Fastest to a first demo. Fails as above.

### Option B — Both in lockstep, feature by feature, against a shared layer
Every feature is specified once, its shared logic written once, and both implementations land in the same
pull request.

### Option C — A cross-framework component library (Web Components / Stencil / Lit)
Write UI components once, consume in both. Genuinely deduplicates the *view* layer too.

Rejected because it defeats the purpose. If the components are framework-neutral custom elements, neither
application demonstrates idiomatic React or idiomatic Angular — both become thin hosts around the same
widgets. It also brings real friction (form integration, change detection at the boundary, SSR, typing).
**The duplication here is the deliverable**, and Option C removes exactly the thing being demonstrated.

## Decision

**Option B.** No feature is complete until it exists in both frameworks, and no phase is complete until the
same end-to-end specs pass against both applications.

### The five-step sequence, per feature slice

1. **Specify once** — `/web/ui-spec/<feature>.md`: routes, states, components, validation rules,
   loading/empty/error behaviour, and the permissions gating it. Framework-agnostic, written *before* either
   implementation, and the shared source of truth. Ambiguity resolved here is ambiguity that cannot become
   divergence later.
2. **Share once** — `/web/shared`: design tokens, the typed API client generated from the services' OpenAPI
   documents, OIDC configuration, `hasPermission()`, formatters, and validation schemas. Framework-neutral
   TypeScript consumed by React, Angular, **and** React Native.
3. **Implement both, idiomatically** — React with hooks, TanStack Query for server state, Redux Toolkit for
   client state. Angular with standalone components, signals, NgRx, and reactive forms. Deliberately *not*
   the same architecture, because the point is that each ecosystem's idioms differ.
4. **Prove parity objectively** — the Playwright suite is written once with a parameterised base URL and run
   twice in CI, against React and against Angular. `web/parity-checklist.md` tracks every screen and
   behaviour.
5. **Report the divergences** — `docs/react-vs-angular.md`, written while the memory is fresh.

### What goes in `/web/shared`, and what must not

**In:** the API client, auth/OIDC, permission helpers, validation schemas, formatters, design tokens, domain
types — everything that would otherwise be written twice and drift.

**Out:** anything rendering, anything holding UI state, anything framework-aware. The moment `/web/shared`
imports from `react` or `@angular/core`, it has stopped being shared.

**The dividing line: `/web/shared` is what the applications *know*; the frameworks own what the applications
*show*.**

### Why parity is proven by tests rather than asserted

A checklist is a claim; a passing test suite is evidence. Running identical specs against both applications
means any behavioural difference fails CI — including differences nobody thought to look for. It also forces
both implementations to expose the same accessible names and roles, which drags accessibility up as a side
effect: a spec written against roles and labels rather than CSS selectors cannot pass against a
div-soup implementation.

Sharing one BFF per client family ([ADR-0006](0006-yarp-gateway-and-bff-per-client.md)) is what makes this
airtight — both applications consume identical endpoints and identical payloads, so any divergence is
unambiguously client-side.

## Consequences

### What this buys us
- Neither implementation can be quietly abandoned.
- Both are idiomatic, because both are written as first implementations rather than translations.
- Shared logic exists once, so it cannot drift.
- Parity is machine-verified, continuously.
- `docs/react-vs-angular.md` accumulates genuine comparative material written at the moment of decision —
  probably the single most useful interview artefact in the repository.
- The React Native app reuses `/web/shared` unchanged, so the third client is much cheaper than the second.

### What this costs us
- **Roughly double the frontend effort per feature.** Unavoidable, and it is the requirement.
- **Feature velocity is bounded by the slower framework**, so progress looks slower than a
  React-first plan would.
- **Context-switching cost** — moving between hooks and signals in one work session is genuinely taxing.
- **The shared layer is a coupling point.** A breaking change there breaks four applications at once. This
  is the correct trade (it is also what keeps them consistent), but it means `/web/shared` needs the most
  careful review of any code in `/web`.
- **CI runs longer** — four builds plus two full e2e passes.
- **The e2e suite must be written against roles and accessible names**, never framework-specific selectors,
  or it cannot run against both. A constraint that improves the tests, but a constraint.

### What we will have to revisit
If the two implementations ever need to diverge deliberately — a framework-specific capability worth
exploiting — the parity checklist gains an "intentionally different" column and the shared specs get
per-app annotations. Silent divergence remains a defect; *declared* divergence is legitimate.

## References

- [`CONTRIBUTING.md` — React and Angular move in lockstep](../../CONTRIBUTING.md#2-react-and-angular-move-in-lockstep)
- [`web/parity-checklist.md`](../../web/parity-checklist.md)
- [ADR-0006](0006-yarp-gateway-and-bff-per-client.md) — one BFF per client experience, not per framework
- [ADR-0008](0008-monorepo.md) — the monorepo that makes same-PR delivery possible
