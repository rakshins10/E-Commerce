# End-to-end tests

**Arrives in Phase 3**, with the first screens.

One Playwright suite, written once, **run twice** — against the React app and against the Angular app. This is
the objective proof of parity required by
[ADR-0014](../../docs/adr/0014-react-and-angular-in-lockstep.md); the
[parity checklist](../../web/parity-checklist.md) is a claim, and this is the evidence.

## How the same suite targets both

The base URL is parameterised, and CI runs the job twice from a matrix:

```ts
// playwright.config.ts
export default defineConfig({
  use: { baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000' },
});
```

```yaml
# .github/workflows/ci.yml
matrix:
  include:
    - { target: react,   base_url: 'http://localhost:3000' }
    - { target: angular, base_url: 'http://localhost:4200' }
```

A behavioural difference between the two implementations fails CI — **including differences nobody thought to
check for**, which is the part a checklist can never give you.

## The constraint that makes it work

**Specs must query by role and accessible name, never by CSS selector or test id.**

```ts
await page.getByRole('button', { name: 'Add to basket' }).click();   // ✅ works against both
await page.locator('.btn-primary').click();                          // ❌ framework-specific
```

Selectors and test ids differ between two independent implementations, so a suite written against them could
only ever run against one. Querying by role forces both apps to expose the same accessible structure — which
means **accessibility is enforced as a side effect of the parity requirement**, and a div-soup implementation
simply cannot pass.

## Fixtures

Tests authenticate against the real Keycloak in the running stack, using the seed users
([`identity/keycloak/realm-export.json`](../../identity/keycloak/), Phase 2) — not a mocked token. Testing
authorisation against a fake token proves nothing about whether the real flow works.
