# Frontend documentation

> **Related:** [`web/README.md`](../../web/README.md) ·
> [`web/parity-checklist.md`](../../web/parity-checklist.md) ·
> [ADR-0014](../adr/0014-react-and-angular-in-lockstep.md) · [React vs Angular](../react-vs-angular.md)

A screen-by-screen catalogue for the storefront and the admin panel, plus the shared layer all five clients
consume.

## Unified with `/web/ui-spec`, not duplicated

The screen catalogue and the UI specs would otherwise say the same thing in two places and drift. They are
**one set of documents**: `/web/ui-spec/<feature>.md` is the source, and these pages index and cross-link
them rather than restating them. A spec that describes a screen *is* that screen's documentation.

## What each screen page covers

- Purpose and route
- Who may access it, and **which permission gates it**
- Layout, and responsive behaviour at each breakpoint
- Every component used, by role
- Every user action, what it triggers, and its in-flight feedback
- The API calls behind it
- State management approach — **and how React's and Angular's differ for this screen**
- All states: loading, empty, error, unauthorised, success
- Validation rules and their exact messages
- Accessibility notes: heading structure, focus management, live regions, keyboard interaction

## Structure

| Page | Covers | Arrives |
|------|--------|---------|
| `shared-layer.md` | Design tokens, generated API client, OIDC flow, permission helpers — documented once | Phase 3 |
| `storefront/` | Browse, search, product detail, basket, checkout, order history, My Account | Phase 3–6 |
| `admin/` | Catalog, orders, inventory, users, roles, dashboard, audit log | Phase 8–10 |

## Two things that hold across every screen

**Accessible names are a contract.** The shared Playwright suite runs against both applications, so it queries
by role and accessible name rather than by CSS selector or test id — those would differ between frameworks.
That makes every accessible name part of the specification, and it means a div-soup implementation cannot pass
the suite. The parity requirement drags accessibility up as a side effect.

**The server is the only enforcement point.** Screens hide actions the token does not permit, and guards
prevent navigation to pages the user cannot use. That is UX, not security — anyone can call the API directly
with the same token. Every permission enforced here is independently enforced server-side, and
[`authorization-model.md`](../authorization-model.md) lists both sides for every permission so the pair can be
checked.
