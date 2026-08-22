/**
 * Where this deployment's storefront BFF and identity provider live.
 *
 * A browser bundle cannot read environment variables, so the usual SPA answer is to bake the addresses
 * in at BUILD time. That means one image per environment - and then staging proves nothing about
 * production, because they are literally different artifacts. This app is built once and TOLD where to
 * point when its container starts (see docs/adr/0023).
 *
 * Three sources, in order, and every one of them is a real code path:
 *
 *   1. `window.__ECOMMERCE_CONFIG__` - written by `config.js`, which the container's entrypoint
 *      generates from environment variables before nginx serves its first byte. Docker Compose and
 *      Kubernetes both use this.
 *   2. `import.meta.env.VITE_BFF_URL` - Vite's compile-time substitution, for `npm run dev`, which has
 *      no container and therefore no entrypoint.
 *   3. The literal below - so a fresh clone with no `.env` still runs against the default compose ports.
 *
 * Never put a secret in any of the three. Everything here ships to the browser and is readable in
 * devtools, which is exactly why this is a *public* OIDC client with no client secret (ADR-0005).
 */

interface RuntimeConfig {
  bffBaseUrl: string;
  keycloakAuthority: string;
}

declare global {
  interface Window {
    __ECOMMERCE_CONFIG__?: Partial<RuntimeConfig>;
  }
}

/**
 * An unsubstituted placeholder is NOT a value.
 *
 * If `config.js` ever reaches a browser still containing `${BFF_BASE_URL}`, treating it as
 * configured would send every request to a URL made of literal dollar signs, and the failure would
 * surface far from its cause. The entrypoint refuses to start when a variable is missing, so this
 * should be unreachable - which is exactly why it is worth two lines rather than a debugging session.
 */
function usable(value: string | undefined): value is string {
  return typeof value === 'string' && value.length > 0 && !value.startsWith('${');
}

function resolve(runtime: string | undefined, build: string | undefined, fallback: string): string {
  if (usable(runtime)) return runtime;
  if (usable(build)) return build;
  return fallback;
}

const runtime = typeof window === 'undefined' ? undefined : window.__ECOMMERCE_CONFIG__;

/** The storefront BFF. Every API call in this app goes through it - services are never called directly. */
export const bffBaseUrl = resolve(
  runtime?.bffBaseUrl,
  import.meta.env.VITE_BFF_URL,
  'http://localhost:6001',
);

/** The Keycloak realm this app authenticates against. */
export const keycloakAuthority = resolve(
  runtime?.keycloakAuthority,
  import.meta.env.VITE_KEYCLOAK_AUTHORITY,
  'http://localhost:8080/realms/ecommerce',
);
