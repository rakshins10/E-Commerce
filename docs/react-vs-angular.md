# React vs Angular, feature by feature

> **Related:** [ADR-0014 — build both in lockstep](adr/0014-react-and-angular-in-lockstep.md) ·
> [`web/parity-checklist.md`](../web/parity-checklist.md)

Written **as each feature is built**, while the memory of what was awkward is fresh. Retrospective comparisons
turn into generic listicles; notes taken at the moment of the decision do not.

The same storefront and the same admin panel are implemented in both frameworks, against the same shared
layer, satisfying the same specs and the same end-to-end tests. That makes this an unusually controlled
comparison: the requirements are identical by construction, so every difference recorded here is genuinely
attributable to the framework rather than to differing scope.

## What each entry will cover

For every feature slice:

- **What the feature is**, and which parity-checklist rows it covers
- **How it was solved in React** — hooks, TanStack Query, Redux Toolkit, React Router
- **How it was solved in Angular** — standalone components, signals, NgRx, reactive forms, DI
- **Where Angular was cleaner**, specifically and with the code
- **Where React was cleaner**, likewise
- **What each forced a workaround for**
- **Which took longer, and why**

## Themes expected to recur

Recorded now as hypotheses, to be confirmed or contradicted by what actually happens — an honest comparison
should be able to surprise its author:

| Area | Expectation |
|------|-------------|
| Server state | TanStack Query is purpose-built for it; NgRx needs more ceremony for the same caching and invalidation. Angular's `resource()`/`httpResource` narrows this. |
| Client state | Redux Toolkit vs NgRx are close cousins; signals may make much of NgRx unnecessary for local state. |
| Forms | Angular reactive forms are strongly typed and declarative out of the box; React needs a library (React Hook Form) to reach parity. |
| DI | Angular's hierarchical injector is a genuine architectural tool; React's context is a weaker substitute frequently misused as one. |
| Change detection | Signals give fine-grained reactivity with no dependency arrays; React's model is simpler to explain but `useEffect` dependencies are a recurring source of bugs. |
| Routing | Angular's guards map naturally onto permission gating; React Router needs a wrapper component to achieve the same. |
| Bundle size | React's baseline is smaller; Angular's build optimiser narrows the gap on a large app. |
| Boilerplate | Angular asks for more upfront structure; React accrues more ad-hoc structure later. |

## Entries

_Populated from Phase 3 onward, one per feature slice._
