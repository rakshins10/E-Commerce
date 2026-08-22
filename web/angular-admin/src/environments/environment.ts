/**
 * Where this deployment's admin BFF and identity provider live.
 *
 * A browser bundle cannot read environment variables, so the usual Angular answer is `fileReplacements`
 * in angular.json: a different environment.ts is swapped in at BUILD time. That means one image per
 * environment - and then staging proves nothing about production, because they are literally different
 * artifacts. This app is built once and TOLD where to point when its container starts (ADR-0023).
 *
 * Two sources, and both are real code paths:
 *
 *   1. `window.__ECOMMERCE_CONFIG__` - written by `config.js`, which the container's entrypoint renders
 *      from environment variables before nginx serves its first byte. Docker Compose and Kubernetes
 *      both use this. `ng serve` picks up the committed `public/config.js` with development values.
 *   2. The literals below - the last resort, so a fresh clone still runs against the compose ports.
 *
 * The React apps resolve exactly the same two values in `src/runtime-config.ts`. Each app owns its copy
 * (ADR-0018); the e2e suite is what catches a divergence.
 *
 * Never put a secret here. Everything in this file ships to the user's browser and is readable in
 * devtools - which is precisely why this is a *public* OIDC client with no client secret (ADR-0005).
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
function resolve(value: string | undefined, fallback: string): string {
  const usable = typeof value === 'string' && value.length > 0 && !value.startsWith('${');
  return usable ? value : fallback;
}

const runtime = typeof window === 'undefined' ? undefined : window.__ECOMMERCE_CONFIG__;

export const environment = {
  production: false,
  keycloakAuthority: resolve(runtime?.keycloakAuthority, 'http://localhost:8080/realms/ecommerce'),
  adminBffBaseUrl: resolve(runtime?.bffBaseUrl, 'http://localhost:6002'),
};
