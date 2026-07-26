# Web

Four applications, two frameworks, one user experience.

| Directory | What it is | Arrives |
|-----------|-----------|---------|
| [`design-tokens/`](design-tokens/) | Colours, spacing, typography, radii — the single source of truth for **all five clients** including React Native | Phase 3 |
| [`shared/`](shared/) | Framework-neutral TypeScript: generated API client, OIDC/auth, permission helpers, validation schemas, formatters | Phase 3 |
| [`ui-spec/`](ui-spec/) | Framework-agnostic screen specs that both implementations must satisfy | Phase 3 onward |
| `react-store/` | Storefront — Vite, TanStack Query, Redux Toolkit, React Router | Phase 3 |
| `angular-store/` | Storefront — standalone components, signals, NgRx, Angular Router | Phase 3 |
| `react-admin/` | Admin panel — React | Phase 8 |
| `angular-admin/` | Admin panel — Angular | Phase 8 |
| [`parity-checklist.md`](parity-checklist.md) | Every screen and behaviour × React status × Angular status | live |

---

## The rule that governs everything here

**React and Angular are built in lockstep.** A feature is not done until it exists in both, in the same pull
request, passing the same end-to-end specs. Never build React first and port to Angular later —
[ADR-0014](../docs/adr/0014-react-and-angular-in-lockstep.md) sets out why that plan reliably fails: the port
is never finished, and what does get finished is Angular that a reviewer immediately recognises as translated
React.

### The sequence, per feature

1. **Specify once** → `ui-spec/<feature>.md`. Routes, states, components, validation, loading/empty/error
   behaviour, and the permissions gating it. Framework-agnostic. Written *first*, because ambiguity resolved
   here is ambiguity that cannot become divergence later.
2. **Share once** → `shared/`. Anything that would otherwise be written twice.
3. **Implement both, idiomatically.** Hooks and TanStack Query on one side; signals, DI, and reactive forms on
   the other. Deliberately *not* the same architecture — demonstrating fluency in both is the point, and
   flattening to a lowest common denominator demonstrates neither.
4. **Prove parity** → update [`parity-checklist.md`](parity-checklist.md) and make the shared Playwright specs
   pass against both apps.
5. **Report the divergences** → [`docs/react-vs-angular.md`](../docs/react-vs-angular.md).

---

## What belongs in `shared/`, and what must not

| In | Out |
|----|-----|
| Generated API client (from the BFF's OpenAPI document) | Anything that renders |
| OIDC configuration and token handling | Anything holding UI state |
| `hasPermission()` and the parsed-token helpers | Anything importing `react` or `@angular/core` |
| Validation schemas | Component libraries |
| Formatters (currency, date, address) | Routing |
| Domain types and enums | |

**The dividing line: `shared/` is what the applications *know*; each framework owns what its application
*shows*.** The moment `shared/` imports a framework, it has stopped being shared — and an ESLint rule enforces
that boundary rather than trusting anyone to remember it.

---

## Why the two storefronts share one BFF

A BFF exists per **client experience**, not per **framework**
([ADR-0006](../docs/adr/0006-yarp-gateway-and-bff-per-client.md)). The React and Angular storefronts have
identical UX by requirement, therefore identical data needs, therefore one `storefront-bff`.

This is not just tidiness — it is what makes parity *provable*. Both apps consume the same endpoints returning
the same payloads, so any behavioural difference between them is unambiguously a client-side bug rather than a
backend difference.

---

## Accessibility is a constraint, not a polish pass

Every screen targets **WCAG 2.2 AA**. There is a structural reason this is easier here than usual: the shared
Playwright specs must run against both applications, so they are written against **accessible roles and
names** (`getByRole('button', { name: 'Add to basket' })`) rather than CSS selectors or test ids, which would
differ between frameworks. A suite written that way cannot pass against a div-soup implementation. The parity
requirement drags accessibility up as a side effect.
