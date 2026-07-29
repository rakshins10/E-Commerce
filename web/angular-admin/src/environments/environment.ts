/**
 * Runtime configuration.
 *
 * Angular resolves this at BUILD time, unlike Vite's `import.meta.env` which
 * the React storefront reads. Both end up baked into the bundle — a browser
 * application has no server-side configuration, so "environment variables" in
 * a SPA are always compile-time substitutions.
 *
 * Consequence worth knowing: **never put a secret here.** Anything in this file
 * ships to the user's browser and is readable in devtools. That is precisely
 * why the storefront is a *public* OIDC client with no client secret
 * (docs/adr/0005) — there is nowhere safe to put one.
 *
 * For per-deployment values without rebuilding, the usual pattern is to fetch a
 * small `config.json` at startup instead. Not needed here, where the endpoints
 * are fixed by docker-compose.
 */
export const environment = {
  production: false,
  keycloakAuthority: 'http://localhost:8080/realms/ecommerce',
  // The ADMIN BFF, deliberately not the storefront's on :6001.
  adminBffBaseUrl: 'http://localhost:6002',
};
