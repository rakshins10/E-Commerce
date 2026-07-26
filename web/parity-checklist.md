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
| S1 | App shell — layout, header, footer, theme switch | ✅ | ✅ | ✅ | ✅ |
| S2 | OIDC login (Auth Code + PKCE) | ✅ | ✅ | ✅ | ✅ |
| S3 | Silent renew / refresh-token rotation | ✅ | ✅ | ✅ | ⬜ |
| S4 | Logout | ✅ | ✅ | ✅ | ✅ |
| S5 | Product list — browse | ✅ | ✅ | ✅ | ✅ |
| S6 | Search & filter (URL-driven, shareable) | ✅ | ✅ | ✅ | ✅ |
| S7 | Product detail | ✅ | ✅ | ✅ | ✅ |
| S8 | Basket — view, update quantity, remove | ✅ | ✅ | ✅ | ✅ |
| S9 | Basket — optimistic add-to-basket | ✅ | ✅ | ✅ | ✅ |
| S10 | Checkout — address (payment arrives in Phase 7) | ✅ | ✅ | ✅ | ✅ |
| S11 | Order confirmation | ✅ | ✅ | ✅ | ✅ |
| S12 | Order history | ✅ | ✅ | ✅ | ✅ |
| S13 | Order detail with status **and saga** timeline | ✅ | ✅ | ✅ | ✅ |
| S14 | My Account — profile | ✅ | ✅ | ✅ | ✅ |
| S15 | My Account — addresses | ✅ | ✅ | ✅ | ✅ |
| S16 | My Account — preferences (locale, currency, theme, opt-ins) | ✅ | ✅ | ✅ | ✅ |
| S17 | 403 / 404 / error boundary | ✅ | ✅ | ✅ | ✅ |
| S18 | Navigation with `aria-current` on the active link | ✅ | ✅ | ✅ | ✅ |
| S19 | Deep link survives a full page reload (SPA fallback) | ✅ | ✅ | ✅ | ✅ |
| S20 | Keyboard skip link is the first focusable element | ✅ | ✅ | ✅ | ✅ |

## Admin

| # | Screen / behaviour | Spec | React | Angular | e2e |
|---|--------------------|------|-------|---------|-----|
| A1 | App shell — permission-aware navigation | ✅ | ✅ | ✅ | ✅ |
| A2 | Permission-gated routing + 403 view | ✅ | ✅ | ✅ | ✅ |
| A3 | Shared data table (paging/sorting arrive with the volume that needs them) | ✅ | ✅ | ✅ | ✅ |
| A4 | Catalog — product list | ⬜ | ⬜ | ⬜ | ⬜ |
| A5 | Catalog — product create/edit | ⬜ | ⬜ | ⬜ | ⬜ |
| A6 | Catalog — categories & brands | ⬜ | ⬜ | ⬜ | ⬜ |
| A7 | Catalog — image upload | ⬜ | ⬜ | ⬜ | ⬜ |
| A8 | Catalog — bulk actions | ⬜ | ⬜ | ⬜ | ⬜ |
| A9 | Orders — list (search & filter deferred) | ✅ | ✅ | ✅ | ✅ |
| A10 | Orders — detail with saga step timeline | ✅ | ✅ | ✅ | ✅ |
| A11 | Orders — status change | ✅ | ✅ | ✅ | ✅ |
| A12 | Orders — refund / cancel | ⬜ | ⬜ | ⬜ | ⬜ |
| A13 | Inventory — stock levels | ✅ | ✅ | ✅ | ✅ |
| A14 | Inventory — adjustments | ✅ | ✅ | ✅ | ✅ |
| A15 | Inventory — low-stock view | ✅ | ✅ | ✅ | ✅ |
| A16 | Users — search | ✅ | ✅ | ✅ | ✅ |
| A17 | Users — detail with role management | ✅ | ✅ | ✅ | ✅ |
| A18 | Users — enable / disable | ✅ | ✅ | ✅ | ✅ |
| A19 | Users — assign roles & groups | ✅ | ✅ | ✅ | ✅ |
| A20 | Users — trigger password reset | ⬜ | ⬜ | ⬜ | ⬜ |
| A21 | Roles & permissions — composite role explorer | ⬜ | ⬜ | ⬜ | ⬜ |
| A22 | Dashboard — sales/orders KPIs | ✅ | ✅ | ✅ | ✅ |
| A23 | Audit log | ✅ | ✅ | ✅ | ✅ |

## Cross-cutting

| # | Behaviour | React | Angular | Notes |
|---|-----------|-------|---------|-------|
| X1 | Design tokens applied; palettes verified identical | ✅ | ✅ | Each app owns its own `tokens.css` (ADR-0018); `scripts/check-design-tokens.mjs` asserts they match and meet WCAG AA |
| X2 | Loading / empty / error states on every data view | ✅ | ✅ | Skeletons, empty state, retryable errors |
| X3 | Responsive layout — mobile, tablet, desktop | ⬜ | ⬜ | |
| X4 | WCAG 2.2 AA — keyboard, focus order, contrast, labels | ✅ | ✅ | Live-region result counts, text-not-colour stock, contrast guarded by script |
| X5 | Correlation id sent on every request | ⬜ | ⬜ | |
| X6 | `hasPermission()` from the shared layer, never a local copy | ⬜ | ⬜ | |
| X7 | Server remains the only real enforcement point | ⬜ | ⬜ | UI hiding is UX, not security |

---

## Intentional divergences

None. The two apps behave identically on every row above, proven by the same 68 Playwright specs passing
against both.

Note that *implementation* differences are expected and desirable — React uses hooks and `useMemo`, Angular
uses an injectable service and `computed()`. Those are recorded in
[`docs/react-vs-angular.md`](../docs/react-vs-angular.md), not here. This table tracks **behaviour**, and
behaviour must match.

Anything landing in this section must link to a justification. **Silent divergence is a defect; declared
divergence is a decision.**

---

## How parity is actually enforced

Since [ADR-0018](../docs/adr/0018-self-contained-frontends.md) each app owns its own copy of the supporting
code, so structure no longer prevents drift. Four mechanisms replace it:

| Mechanism | Catches |
|-----------|---------|
| [`tests/e2e`](../tests/e2e/) — 53 storefront + 15 back-office specs, each run against both apps | Behavioural drift, including differences nobody thought to check |
| **Unit tests — the same 20 assertions in each app** ([react](react-store/src/lib/lib.spec.ts), [angular](angular-store/src/app/core/core.spec.ts)) | Logic drift in the duplicated `permissions` and `formatting` modules — a currency separator, an off-by-one in `truncate`, a permission helper's empty-argument behaviour |
| [`scripts/check-design-tokens.mjs`](../scripts/check-design-tokens.mjs) | Visual drift, and any WCAG AA contrast regression |
| This checklist | Scope drift — a feature built in one app and not the other |

The unit tests are **deliberately identical files**. If one copy of a module changes behaviour, exactly one
of the two suites goes red and the diff points straight at the divergence. That is the whole trick: the e2e
suite catches drift that is visible on screen, and this catches drift that is not.

```bash
cd web && npm test --workspace react-store && npm test --workspace angular-store
cd ../tests/e2e && npm run test:react && npm run test:angular
node ../../scripts/check-design-tokens.mjs
```
