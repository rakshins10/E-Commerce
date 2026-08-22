# ADR-0023 - A single-page app reads its configuration at runtime, not at build time

**Status:** Accepted · **Date:** 2026-08-20 · **Phase:** 12

---

## Context

Both storefronts and both admin panels bake their two external addresses into the JavaScript bundle:

```ts
// angular-store/src/environments/environment.ts
export const environment = {
  keycloakAuthority: 'http://localhost:8080/realms/ecommerce',
  bffBaseUrl: 'http://localhost:6001',
};
```

```dotenv
# react-store/.env
VITE_BFF_URL=http://localhost:6001
```

The Angular file even documents why: *"a browser application has no server-side configuration, so
environment variables in a SPA are always compile-time substitutions."* That is true of the *bundle*. It
is not true of the *container*, and the difference is the whole of this decision.

Phase 12 deploys to Kubernetes, where those addresses are wrong. In a cluster the BFF is not on
`localhost:6001` - it is behind an ingress, on a hostname that differs between a local cluster, a staging
cluster and production.

That leaves two options, and only one of them is compatible with how container promotion works:

1. **Build an image per environment.** `react-store:staging` and `react-store:production` become different
   images built from the same commit.
2. **Build one image and configure it at start.**

Option 1 breaks the property that makes container deployments trustworthy: **the artifact you tested is
the artifact you ship.** If staging and production run different images, staging proves nothing about
production - it proves something about a build that no longer exists. It also multiplies the release
pipeline by the number of environments, and quietly invites "just rebuild it" as a fix for a production
incident.

## Decision

**One image per app. The addresses arrive when the container starts.**

Each web image ships a template and substitutes it at container start, before nginx accepts a request:

```
/usr/share/nginx/html/config.js.template   ->   envsubst   ->   /usr/share/nginx/html/config.js
```

```js
// config.js.template
window.__ECOMMERCE_CONFIG__ = {
  bffBaseUrl: "${BFF_BASE_URL}",
  keycloakAuthority: "${KEYCLOAK_AUTHORITY}",
};
```

`index.html` loads `config.js` **before** the application bundle, so `window.__ECOMMERCE_CONFIG__` exists
by the time any module runs. Each app reads it through one accessor with the compile-time value as a
fallback:

```ts
const runtime = window.__ECOMMERCE_CONFIG__;
const bffBaseUrl = runtime?.bffBaseUrl || import.meta.env.VITE_BFF_URL || 'http://localhost:6001';
```

**The fallback chain is deliberate, and it is what keeps this cheap.** `npm run dev` has no container and
therefore no `config.js`; it falls through to the compile-time value and behaves exactly as before. Docker
Compose gets real values from its `environment:` block. Kubernetes gets them from a ConfigMap. One image,
three ways of telling it where to point, and no code path that only one environment exercises.

### Why `envsubst` at start, and not a fetch

The obvious alternative is for the app to `fetch('/config.json')` during bootstrap. It is a real pattern
and it works, but it costs a network round trip before the app can render *anything*, and it puts an
async failure mode on the critical path of every page load: if that fetch fails, the app has no idea
where its API is and cannot even show a sensible error, because it does not know where to report it.

`envsubst` runs once, in the entrypoint, before nginx serves its first byte. By the time a browser can
ask for anything, the answer is already a static file. Nothing is async, nothing can half-fail.

## What this costs

**A new failure mode: an unset variable becomes an empty string.** `envsubst` does not know that
`${BFF_BASE_URL}` is required; if the deployment forgets it, the app boots and every API call goes to a
relative empty URL. The entrypoint therefore **fails loudly** if either variable is missing, because a
container that refuses to start is diagnosable and one that silently misroutes every request is not.

**`index.html` gains a blocking script.** `config.js` must load before the bundle, so it is one
render-blocking request. It is a handful of bytes served from the same origin, and it is the price of not
having a race between configuration and the code that needs it.

**The template is not type-checked.** A typo in `config.js.template` surfaces at runtime, not at compile
time. Mitigated by the accessor being one function per app with a declared TypeScript type, so everything
downstream of that boundary is checked normally.

**Two more things to keep in step across four apps** (ADR-0018 - each app owns its copy). The e2e suite is
what catches a divergence, as ever.

## Alternatives rejected

**A build argument per environment** - argued above; it breaks artifact promotion.

**Reading configuration from the BFF** (`GET /api/config`). Tidy, and it makes the BFF a hard dependency
of *rendering*, not just of data. The app could then never display "the API is unreachable", because
finding that out requires reaching the API.

**Serving the SPA from the BFF itself**, so the API is always same-origin and needs no configuration at
all. Genuinely attractive - it removes CORS entirely - but it couples the release of a front end to the
release of a gateway, and it means a static asset is served by a .NET process rather than by nginx, which
is worse at it in every measurable way.
