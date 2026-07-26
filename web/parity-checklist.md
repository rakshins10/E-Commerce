# React / Angular parity checklist

Every screen and every behaviour, with its status in each framework. **A phase is not complete until this
table has no gaps** — see [ADR-0014](../docs/adr/0014-react-and-angular-in-lockstep.md).

This checklist is a *claim*. The objective proof is the shared Playwright suite in
[`tests/e2e`](../tests/e2e/README.md), written once and run against both applications by CI. If the checklist and the
test results disagree, the tests are right.

**Legend:** ✅ done · 🚧 in progress · ⬜ not started · ➖ not applicable · ⚠️ intentionally different (must
link to a justification)

---

## Storefront

| # | Screen / behaviour | Spec | React | Angular | e2e |
|---|--------------------|------|-------|---------|-----|
| S1 | App shell — layout, header, footer, theme switch | ⬜ | ⬜ | ⬜ | ⬜ |
| S2 | OIDC login (Auth Code + PKCE) | ⬜ | ⬜ | ⬜ | ⬜ |
| S3 | Silent renew / refresh-token rotation | ⬜ | ⬜ | ⬜ | ⬜ |
| S4 | Logout | ⬜ | ⬜ | ⬜ | ⬜ |
| S5 | Product list — browse | ⬜ | ⬜ | ⬜ | ⬜ |
| S6 | Search & filter | ⬜ | ⬜ | ⬜ | ⬜ |
| S7 | Product detail | ⬜ | ⬜ | ⬜ | ⬜ |
| S8 | Basket — view, update quantity, remove | ⬜ | ⬜ | ⬜ | ⬜ |
| S9 | Basket — optimistic add-to-basket | ⬜ | ⬜ | ⬜ | ⬜ |
| S10 | Checkout — address & payment | ⬜ | ⬜ | ⬜ | ⬜ |
| S11 | Order confirmation | ⬜ | ⬜ | ⬜ | ⬜ |
| S12 | Order history | ⬜ | ⬜ | ⬜ | ⬜ |
| S13 | Order detail with status timeline | ⬜ | ⬜ | ⬜ | ⬜ |
| S14 | My Account — profile | ⬜ | ⬜ | ⬜ | ⬜ |
| S15 | My Account — addresses | ⬜ | ⬜ | ⬜ | ⬜ |
| S16 | My Account — preferences (locale, currency, theme, opt-ins) | ⬜ | ⬜ | ⬜ | ⬜ |
| S17 | 403 / 404 / error boundary | ⬜ | ⬜ | ⬜ | ⬜ |

## Admin

| # | Screen / behaviour | Spec | React | Angular | e2e |
|---|--------------------|------|-------|---------|-----|
| A1 | App shell — permission-aware navigation | ⬜ | ⬜ | ⬜ | ⬜ |
| A2 | Permission-gated routing + 403 view | ⬜ | ⬜ | ⬜ | ⬜ |
| A3 | Shared data table — server-side paging, sorting, filtering | ⬜ | ⬜ | ⬜ | ⬜ |
| A4 | Catalog — product list | ⬜ | ⬜ | ⬜ | ⬜ |
| A5 | Catalog — product create/edit | ⬜ | ⬜ | ⬜ | ⬜ |
| A6 | Catalog — categories & brands | ⬜ | ⬜ | ⬜ | ⬜ |
| A7 | Catalog — image upload | ⬜ | ⬜ | ⬜ | ⬜ |
| A8 | Catalog — bulk actions | ⬜ | ⬜ | ⬜ | ⬜ |
| A9 | Orders — search & filter | ⬜ | ⬜ | ⬜ | ⬜ |
| A10 | Orders — detail with saga step timeline | ⬜ | ⬜ | ⬜ | ⬜ |
| A11 | Orders — status change | ⬜ | ⬜ | ⬜ | ⬜ |
| A12 | Orders — refund / cancel | ⬜ | ⬜ | ⬜ | ⬜ |
| A13 | Inventory — stock levels | ⬜ | ⬜ | ⬜ | ⬜ |
| A14 | Inventory — adjustments | ⬜ | ⬜ | ⬜ | ⬜ |
| A15 | Inventory — low-stock view | ⬜ | ⬜ | ⬜ | ⬜ |
| A16 | Users — search | ⬜ | ⬜ | ⬜ | ⬜ |
| A17 | Users — detail (profile + preferences) | ⬜ | ⬜ | ⬜ | ⬜ |
| A18 | Users — enable / disable | ⬜ | ⬜ | ⬜ | ⬜ |
| A19 | Users — assign roles & groups | ⬜ | ⬜ | ⬜ | ⬜ |
| A20 | Users — trigger password reset | ⬜ | ⬜ | ⬜ | ⬜ |
| A21 | Roles & permissions — composite role explorer | ⬜ | ⬜ | ⬜ | ⬜ |
| A22 | Dashboard — sales/orders KPIs | ⬜ | ⬜ | ⬜ | ⬜ |
| A23 | Audit log | ⬜ | ⬜ | ⬜ | ⬜ |

## Cross-cutting

| # | Behaviour | React | Angular | Notes |
|---|-----------|-------|---------|-------|
| X1 | Design tokens applied from the shared source | ⬜ | ⬜ | No hardcoded colours or spacing in either app |
| X2 | Loading / empty / error states on every data view | ⬜ | ⬜ | |
| X3 | Responsive layout — mobile, tablet, desktop | ⬜ | ⬜ | |
| X4 | WCAG 2.2 AA — keyboard, focus order, contrast, labels | ⬜ | ⬜ | Enforced by e2e specs querying by role and name |
| X5 | Correlation id sent on every request | ⬜ | ⬜ | |
| X6 | `hasPermission()` from the shared layer, never a local copy | ⬜ | ⬜ | |
| X7 | Server remains the only real enforcement point | ⬜ | ⬜ | UI hiding is UX, not security |

---

## Intentional divergences

None yet. Anything landing here must link to a note in
[`docs/react-vs-angular.md`](../docs/react-vs-angular.md) explaining why matching behaviour was the wrong
call. **Silent divergence is a defect; declared divergence is a decision.**
