# Web

Two applications, two frameworks, one user experience — and **each one owns every line it needs**.

| Directory | What it is | Arrives |
|-----------|-----------|---------|
| [`react-store/`](react-store/) | Storefront — React 19, Vite, React Router | ✅ shell + OIDC |
| [`angular-store/`](angular-store/) | Storefront — Angular 22, standalone, signals | ✅ shell + OIDC |
| `react-admin/` | Admin panel — React | Phase 8 |
| `angular-admin/` | Admin panel — Angular | Phase 8 |
| [`ui-spec/`](ui-spec/) | Framework-agnostic screen specs both implementations satisfy | ongoing |
| [`parity-checklist.md`](parity-checklist.md) | Every screen and behaviour × React status × Angular status | live |

---

## Each app is self-contained

**Nothing in `react-store` imports from outside itself. Same for `angular-store`.** There is no shared
package.

That is a deliberate reversal of the usual advice, and it is recorded in
[ADR-0018](../docs/adr/0018-self-contained-frontends.md). The reasoning: this repository exists to be
*studied*. When code lives in a shared package, understanding how authentication works in the React app
means reading three places — the component, the hook, and a package outside the application. For a codebase
whose job is to be read and explained, that indirection is a cost paid on every reading.

So each app owns its own copy:

| Concern | React | Angular |
|---------|-------|---------|
| Permissions | `src/lib/permissions.ts` | `src/app/core/permissions.ts` |
| OIDC config, token parsing | `src/lib/auth.ts` | `src/app/core/auth-config.ts` |
| Formatters | `src/lib/formatting.ts` | `src/app/core/formatting.ts` |
| API client | `src/lib/api-client.ts` | `src/app/core/api-client.ts` |
| Design tokens | `src/styles/tokens.css` | `src/styles/tokens.css` |

**The honest cost:** every logic change is made twice, and nothing structurally stops the two drifting apart.

---

## How drift is caught instead

Since structure no longer prevents divergence, tests do:

**1. The shared end-to-end suite** — [`tests/e2e`](../tests/e2e/) runs **identical specs against both apps**,
with only the base URL differing. It asserts on visible text and accessible names, so a divergence in
permission parsing, formatting, or labelling fails CI. This tests *behaviour*, which is what actually
matters, rather than checking that two files match.

```bash
cd tests/e2e
npm run test:react      # 9 specs against :3000
npm run test:angular    # the same 9 against :4200
```

**2. The token guard** — [`scripts/check-design-tokens.mjs`](../scripts/check-design-tokens.mjs) validates
both palettes against WCAG 2.2 AA contrast **and** asserts they are byte-identical. Visual drift is the
hardest for a test to notice and the easiest for a user to.

```bash
node scripts/check-design-tokens.mjs
```

**3. The parity checklist** — [`parity-checklist.md`](parity-checklist.md), the human record.

---

## Still built in lockstep

[ADR-0014](../docs/adr/0014-react-and-angular-in-lockstep.md) is unchanged and now matters *more*. Every UI
feature lands in **both** frameworks in the **same pull request**. Never build React first and port later:
the port is never finished, and what does get finished is Angular a reviewer immediately recognises as
translated React.

The sequence per feature:

1. **Specify once** — `ui-spec/<feature>.md`: routes, states, components, validation, loading/empty/error
   behaviour, and the permissions gating it. Written *before* either implementation.
2. **Implement both, idiomatically** — hooks and TanStack Query on one side; signals, DI and reactive forms
   on the other. Deliberately *not* the same architecture.
3. **Prove parity** — update the checklist and make the shared specs pass against both.
4. **Record the divergences** — [`docs/react-vs-angular.md`](../docs/react-vs-angular.md).

---

## Running them

```bash
# In containers, alongside Keycloak (needed for login)
cd deploy && docker compose up -d --wait
#   React    http://localhost:3000
#   Angular  http://localhost:4200

# Or outside containers, for development
cd web
npm install
npm run dev:react      # :3000
npm run dev:angular    # :4200
```

Sign in with any seed user — password `Passw0rd!`. Try `customer` (5 permissions) then `administrator`
(15) and watch the page change.

> Changing a front-end file and running `docker compose up -d` shows you the **old** app. The four web
> services are `build:` targets, so `docker compose build react-store angular-store react-admin
> angular-admin` comes first.

---

## Product imagery

Twelve products, thirteen SVGs, 53 KB for the lot.

[`scripts/generate-product-images.mjs`](../scripts/generate-product-images.mjs) draws them from three
category palettes into `web/shared-assets/img`, and
[`web/scripts/sync-assets.mjs`](scripts/sync-assets.mjs) copies that directory into each app's `public/img`
on `predev` and `prebuild`. The copies are gitignored.

**Why generated SVG rather than committed photographs.** A photograph of a hoodie is a binary blob that a
diff cannot show you, weighs more than this entire application's JavaScript, and would have to be licensed.
These are text: a change to the artwork is a readable diff, and the whole catalogue costs less than one
JPEG.

**Why a sync script rather than a shared package.** [ADR-0018](../docs/adr/0018-self-contained-frontends.md)
says each app is self-contained, and a Vite or Angular build can only serve what is inside its own project.
Copying is the honest version of that constraint: one source directory, four generated copies, and the
script deletes each target first so a removed image does not linger in three apps.

---

## Accessibility is a constraint, not a polish pass

Every screen targets **WCAG 2.2 AA**, and there is a structural reason it holds: the shared specs must run
against both apps, so they are written against **accessible roles and names**
(`getByRole('button', { name: 'Sign in' })`) rather than CSS selectors or test ids, which would differ
between two independent implementations.

A suite written that way **cannot pass against a div-soup implementation.** Parity enforcement drags
accessibility along with it — and in practice it already has: writing the first spec forced the header to be
a `banner` landmark and the theme toggle to carry a real accessible name.
