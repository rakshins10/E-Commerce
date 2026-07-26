# UI specifications

One Markdown file per feature, written **before** either implementation. This is the framework-agnostic
source of truth that the React app, the Angular app, the React Native app, and the shared Playwright specs
must all satisfy.

## Why specify first

Because the alternative is specifying by accident. If React is built first, its incidental choices — the
wording of a validation message, what an empty list shows, whether the button disables during submit — become
the de facto specification, and the Angular implementation either copies them (a port, which
[ADR-0014](../../docs/adr/0014-react-and-angular-in-lockstep.md) exists to prevent) or quietly differs
(divergence nobody notices until a customer does).

**Ambiguity resolved here is ambiguity that cannot become divergence later.** The spec is also what the e2e
specs are written from, so a behaviour absent from the spec is a behaviour nothing tests.

## What a spec must cover

Every one of these, even when the answer is "nothing special" — writing that down is itself a decision:

- **Route** and whether it is public, authenticated, or permission-gated (name the exact permission).
- **Purpose** — one sentence on what the user is trying to achieve.
- **Layout** — regions and their responsive behaviour at mobile, tablet, desktop.
- **Components** used, by role rather than by framework class name.
- **Every state**: loading, empty, error, unauthorised (403), partial, success.
- **Every user action**, what it triggers, and what the user sees while it is in flight.
- **API calls** behind it, including which are parallel and which sequential.
- **Validation rules and their exact messages** — the messages are part of the contract, because the e2e specs
  assert on them.
- **Optimistic behaviour**, if any, and what a rollback looks like.
- **Accessibility**: heading structure, focus management on navigation and on error, the accessible name of
  every interactive element, and anything needing a live region.

## Accessible names are a contract, not a detail

The shared Playwright suite runs against both applications, so it must query by **role and accessible name**
(`getByRole('button', { name: 'Add to basket' })`) rather than by CSS selector or test id, which would differ
between frameworks. That makes the accessible name part of the specification: change it in one app and the
shared suite fails against that app only, which is exactly the signal wanted.

## Template

```markdown
# <Feature name>

- **Route:** /path/:param
- **Access:** public | authenticated | permission `catalog:write`
- **Spec status:** draft | agreed | implemented
- **Parity checklist rows:** S5, S6

## Purpose
One or two sentences.

## Layout
Regions, and how they reflow at each breakpoint.

## Components
| Component | Role | Accessible name |
|-----------|------|-----------------|

## States
| State | Trigger | What the user sees |
|-------|---------|--------------------|
| Loading | | |
| Empty | | |
| Error | | |
| Unauthorised | | |
| Success | | |

## Actions
| Action | Trigger | Effect | In-flight feedback |
|--------|---------|--------|--------------------|

## Data
| Call | Endpoint | When | Cache / invalidation |
|------|----------|------|----------------------|

## Validation
| Field | Rule | Message |
|-------|------|---------|

## Accessibility
Heading order, focus management, live regions, keyboard interactions.

## Open questions
Anything unresolved. An empty section means the spec is ready to implement.
```

---

Specs arrive from Phase 3 onward, alongside the features they describe. Each is cross-linked from
[`docs/frontend/`](../../docs/frontend/), which holds the same content as the screen catalogue — deliberately
unified rather than duplicated, so there is only one place to keep current.
